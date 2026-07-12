using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModBrowserService : IModBrowserService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModBrowserService>();
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    private readonly Dictionary<string, (List<DownloadableMod> Mods, DateTime Fetched)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public ModBrowserService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EVO-Mod-Manager/1.0");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/xml, text/html, */*");
        HttpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        HttpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public Task<List<ModSource>> GetSourcesAsync()
    {
        return Task.FromResult(ModSource.Defaults);
    }

    public async Task<List<DownloadableMod>> FetchModListAsync(string sourceId, ModType? category = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(sourceId, out var cached) && DateTime.UtcNow - cached.Fetched < CacheTtl)
            return FilterByCategory(cached.Mods, category);

        var source = ModSource.Defaults.FirstOrDefault(s => s.Id == sourceId);
        if (source == null) return new List<DownloadableMod>();

        List<DownloadableMod>? mods = null;

        if (source.SourceType == "rss")
            mods = await FetchFromRssAsync(source.RssFeedUrl, ct);
        else if (source.SourceType == "json")
            mods = await FetchFromJsonRepoAsync(source.DownloadPageUrl, ct);

        if (mods != null && mods.Count > 0)
        {
            _cache[sourceId] = (mods, DateTime.UtcNow);
            Log.Information("Fetched {Count} mods from {Name}", mods.Count, source.Name);
            return FilterByCategory(mods, category);
        }

        return _cache.TryGetValue(sourceId, out var old)
            ? FilterByCategory(old.Mods, category)
            : new List<DownloadableMod>();
    }

    private async Task<List<DownloadableMod>?> FetchFromRssAsync(string feedUrl, CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync(feedUrl, ct);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);
            if (feed == null || !feed.Items.Any()) return null;

            var mods = new List<DownloadableMod>();
            foreach (var item in feed.Items)
            {
                ct.ThrowIfCancellationRequested();
                var category = item.Categories?.FirstOrDefault()?.Name ?? "";
                var title = item.Title?.Text ?? "";

                if (!IsAceEvoRelated(category, title))
                    continue;

                var downloadUrl = ExtractDownloadUrl(item);

                mods.Add(new DownloadableMod
                {
                    Title = title,
                    Author = item.Authors?.FirstOrDefault()?.Name,
                    Published = item.PublishDate.DateTime,
                    Category = category,
                    DownloadUrl = downloadUrl,
                    ResourceId = ExtractResourceId(downloadUrl),
                    Description = StripHtml(item.Content?.ToString() ?? item.Summary?.Text ?? ""),
                    ModType = ClassifyFromCategory(category, title)
                });
            }

            return mods;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RSS fetch failed for {Url}", feedUrl);
            return null;
        }
    }

    private async Task<List<DownloadableMod>?> FetchFromJsonRepoAsync(string repoUrl, CancellationToken ct)
    {
        try
        {
            var json = await HttpClient.GetStringAsync(repoUrl, ct);
            var mods = JsonSerializer.Deserialize<List<JsonRepoEntry>>(json);
            if (mods == null) return null;

            return mods.Select(m => new DownloadableMod
            {
                Title = m.Name,
                Author = m.Author,
                Description = m.Description,
                DownloadUrl = m.DownloadUrl,
                ResourceId = m.Id,
                ModType = m.ModType,
                Published = m.Created
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "JSON repo fetch failed for {Url}", repoUrl);
            return null;
        }
    }

    public async Task<string> DownloadModAsync(DownloadableMod mod, string downloadDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mod.DownloadUrl))
            throw new InvalidOperationException("Mod has no download URL");

        Directory.CreateDirectory(downloadDir);

        var pageUrl = mod.DownloadUrl.StartsWith("http")
            ? mod.DownloadUrl
            : $"https://www.overtake.gg{mod.DownloadUrl}";

        // Strategy 1: Try direct download URL patterns first
        var candidates = new List<string> { pageUrl };

        if (mod.ResourceId != null)
        {
            candidates.Add($"https://www.overtake.gg/attachments/{mod.ResourceId}/download");
            candidates.Add($"https://www.overtake.gg/attachments/{mod.ResourceId}-{SanitizeFileName(mod.Title)}.attach");
        }
        // Add /download suffix
        if (pageUrl.EndsWith("/"))
            candidates.Add(pageUrl + "download");
        else if (!pageUrl.EndsWith("/download"))
            candidates.Add(pageUrl + "/download");

        foreach (var url in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = await TryDirectDownloadAsync(url, downloadDir, mod.Title, progress, ct);
            if (result != null) return result;
        }

        // Strategy 2: Try to scrape the mod page for a direct download link
        var scrapedUrl = await ScrapeDownloadUrlAsync(pageUrl, ct);
        if (scrapedUrl != null)
        {
            var result = await TryDirectDownloadAsync(scrapedUrl, downloadDir, mod.Title, progress, ct);
            if (result != null) return result;
        }

        // Strategy 3: Open in browser as last resort
        Log.Warning("Automated download failed for {Title}, opening in browser", mod.Title);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = pageUrl
        });

        throw new InvalidOperationException(
            $"OverTake.gg blocked the automated download.\n\n" +
            $"The mod page has been opened in your browser.\n" +
            $"Please download manually and drag the file into EVO Mod Manager.");
    }

    private async Task<string?> TryDirectDownloadAsync(string url, string downloadDir, string modTitle,
        IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";

            // Skip if we got HTML (Cloudflare challenge or mod page)
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return null;

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            // Skip redirect pages or very small non-file responses
            if (totalBytes == 0) return null;

            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                           ?? $"{SanitizeFileName(modTitle)}.zip";
            var filePath = Path.Combine(downloadDir, fileName);

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(filePath);

            if (totalBytes > 0)
            {
                var buffer = new byte[8192];
                long bytesRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;
                    progress?.Report((double)bytesRead / totalBytes);
                }
            }
            else
            {
                await contentStream.CopyToAsync(fileStream, ct);
            }

            Log.Information("Downloaded {Title} ({Size}) from {Url}",
                modTitle, totalBytes > 0 ? $"{totalBytes / 1024.0:F1}KB" : "unknown", url);
            return filePath;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ScrapeDownloadUrlAsync(string pageUrl, CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync(pageUrl, ct);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync(ct);

            // Check for Cloudflare/JS challenge
            if (IsCloudflareBlock(html)) return null;

            // Try to find attachment/download links
            var patterns = new[]
            {
                @"href=""(/attachments/[^""]+)""",
                @"href=""(/downloads/[^""]+/download)""",
                @"data-resource-url=""([^""]+)""",
                @"<a[^>]*class=""[^""]*button[^""]*""[^>]*href=""([^""]+download[^""]*)""",
                @"<a[^>]*href=""([^""]+\.(zip|7z|rar|kspkg))""",
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(html, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.StartsWith("http")
                        ? match.Groups[1].Value
                        : $"https://www.overtake.gg{match.Groups[1].Value}";
            }
        }
        catch { }
        return null;
    }

    private static bool IsCloudflareBlock(string html) =>
        html.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase) ||
        html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);

    private static bool IsAceEvoRelated(string category, string title)
    {
        var lower = (category + " " + title).ToLowerInvariant();

        // Must mention EVO or ACE to be an Assetto Corsa EVO mod
        var mentionsEvo = lower.Contains("assetto corsa evo") ||
                          lower.Contains("ace evo") ||
                          lower.Contains("ace car") ||
                          lower.Contains("ace skin") ||
                          lower.Contains("ace app") ||
                          lower.Contains("ace track") ||
                          lower.Contains("ace sound") ||
                          lower.Contains("ace misc");

        // Exclude other sims
        var isOtherSim = lower.Contains("assetto corsa ") &&
                         !lower.Contains("assetto corsa evo");

        return mentionsEvo && !isOtherSim;
    }

    private static List<DownloadableMod> FilterByCategory(List<DownloadableMod> mods, ModType? category)
    {
        if (category == null || category == ModType.Unknown) return mods;
        return mods.Where(m => m.ModType == category).ToList();
    }

    private static string? ExtractDownloadUrl(SyndicationItem item)
    {
        var content = item.Content?.ToString() ?? item.Summary?.Text ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(content,
            @"href=""(/downloads/[^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractResourceId(string? downloadUrl)
    {
        if (downloadUrl == null) return null;
        var match = System.Text.RegularExpressions.Regex.Match(downloadUrl, @"\.(\d+)/");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static ModType ClassifyFromCategory(string? categoryName, string? title)
    {
        var lower = ((categoryName ?? "") + " " + (title ?? "")).ToLowerInvariant();
        if (lower.Contains("car")) return ModType.Car;
        if (lower.Contains("track") || lower.Contains("circuit")) return ModType.Track;
        if (lower.Contains("skin") || lower.Contains("livery")) return ModType.Skin;
        if (lower.Contains("sound") || lower.Contains("audio")) return ModType.Sound;
        if (lower.Contains("app")) return ModType.App;
        return ModType.Misc;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 80 ? name[..80] : name;
    }

    private class JsonRepoEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string DownloadUrl { get; set; } = "";
        public ModType ModType { get; set; }
        public DateTime Created { get; set; }
    }
}
