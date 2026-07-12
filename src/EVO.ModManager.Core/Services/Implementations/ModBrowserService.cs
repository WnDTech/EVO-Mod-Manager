using System.ServiceModel.Syndication;
using System.Xml;
using AngleSharp;
using AngleSharp.Dom;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModBrowserService : IModBrowserService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModBrowserService>();

    private static readonly string[] FeedUrls =
    {
        "https://www.overtake.gg/forums/-/index.rss",
        "https://www.overtake.gg/forums/assetto-corsa-evo-mods.752/-/index.rss?resource=1",
        "https://www.overtake.gg/downloads/categories/assetto-corsa-evo.275/-/index.rss"
    };

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    private static readonly IConfiguration AngleConfig = Configuration.Default.WithDefaultLoader();
    private DateTime _lastFetch = DateTime.MinValue;
    private List<DownloadableMod>? _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    public ModBrowserService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/xml, text/html");
        HttpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
    }

    public async Task<List<DownloadableMod>> FetchModListAsync(ModType? category = null,
        CancellationToken ct = default)
    {
        if (_cache != null && DateTime.UtcNow - _lastFetch < CacheTtl)
            return FilterByCategory(_cache, category);

        List<DownloadableMod>? mods = null;

        foreach (var feedUrl in FeedUrls)
        {
            mods = await TryFetchRssAsync(feedUrl, ct);
            if (mods != null && mods.Count > 0) break;
        }

        if (mods == null || mods.Count == 0)
            mods = await TryScrapeDownloadsPageAsync(ct);

        if (mods != null && mods.Count > 0)
        {
            _cache = mods;
            _lastFetch = DateTime.UtcNow;
            Log.Information("Fetched {Count} mods from OverTake.gg", mods.Count);
            return FilterByCategory(mods, category);
        }

        Log.Warning("No mods fetched from any source, returning cache or empty");
        return _cache ?? new List<DownloadableMod>();
    }

    private async Task<List<DownloadableMod>?> TryFetchRssAsync(string feedUrl, CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync(feedUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("RSS feed {Url} returned {Status}", feedUrl, response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("xml", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("rss", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("RSS feed {Url} returned non-XML content type: {Type}", feedUrl, contentType);
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);
            if (feed == null || !feed.Items.Any()) return null;

            var mods = new List<DownloadableMod>();
            foreach (var item in feed.Items)
            {
                ct.ThrowIfCancellationRequested();
                var category = item.Categories?.FirstOrDefault()?.Name ?? "";

                // Filter: only AC EVO mods
                if (!category.Contains("Assetto Corsa Evo", StringComparison.OrdinalIgnoreCase) &&
                    !category.Contains("ACE", StringComparison.OrdinalIgnoreCase) &&
                    !item.Title?.Text?.Contains("evo", StringComparison.OrdinalIgnoreCase) == true)
                    continue;

                var downloadUrl = ExtractDownloadUrl(item);

                mods.Add(new DownloadableMod
                {
                    Title = item.Title?.Text ?? "Unknown",
                    Author = item.Authors?.FirstOrDefault()?.Name,
                    Published = item.PublishDate.DateTime,
                    Category = category,
                    DownloadUrl = downloadUrl,
                    ResourceId = ExtractResourceId(downloadUrl),
                    Description = StripHtml(item.Content?.ToString() ?? item.Summary?.Text ?? ""),
                    ModType = ClassifyFromCategory(category)
                });
            }

            if (mods.Count > 0)
                Log.Information("RSS feed {Url}: found {Count} mods", feedUrl, mods.Count);

            return mods;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse RSS feed {Url}", feedUrl);
            return null;
        }
    }

    private async Task<List<DownloadableMod>?> TryScrapeDownloadsPageAsync(CancellationToken ct)
    {
        try
        {
            var url = "https://www.overtake.gg/downloads/categories/assetto-corsa-evo.275/";
            var html = await HttpClient.GetStringAsync(url, ct);

            var context = BrowsingContext.New(AngleConfig);
            var document = await context.OpenAsync(req => req.Content(html), ct);

            var mods = new List<DownloadableMod>();
            var entries = document.QuerySelectorAll("article.resource-list--entry, .resource-list-item, [data-resource-id]");

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var titleEl = entry.QuerySelector("h3 a, .resource-title a, a[href*='/downloads/']");
                var title = titleEl?.TextContent?.Trim();
                if (string.IsNullOrEmpty(title)) continue;

                var href = titleEl?.GetAttribute("href");
                var authorEl = entry.QuerySelector(".resource-author a, .username");
                var author = authorEl?.TextContent?.Trim();
                var descEl = entry.QuerySelector(".resource-description, .tagLine, p");
                var desc = descEl?.TextContent?.Trim();

                mods.Add(new DownloadableMod
                {
                    Title = title,
                    Author = author,
                    DownloadUrl = href,
                    ResourceId = ExtractResourceId(href),
                    Description = desc,
                    Category = "ACE Scraped",
                    ModType = ClassifyFromCategory(title)
                });
            }

            if (mods.Count > 0)
                Log.Information("Scraped {Count} mods from downloads page", mods.Count);

            return mods;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to scrape downloads page");
            return null;
        }
    }

    public async Task<string> DownloadModAsync(DownloadableMod mod, string downloadDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(mod.DownloadUrl))
            throw new InvalidOperationException("Mod has no download URL");

        Directory.CreateDirectory(downloadDir);

        var url = mod.DownloadUrl.StartsWith("http")
            ? mod.DownloadUrl
            : $"https://www.overtake.gg{mod.DownloadUrl}";

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                       ?? $"{SanitizeFileName(mod.Title)}.zip";
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

        Log.Information("Downloaded mod {Title} to {Path}", mod.Title, filePath);
        return filePath;
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
        var match = System.Text.RegularExpressions.Regex.Match(downloadUrl, @"\.(\d+)/$");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static ModType ClassifyFromCategory(string? categoryName)
    {
        var lower = (categoryName ?? "").ToLowerInvariant();
        if (lower.Contains("car")) return ModType.Car;
        if (lower.Contains("track")) return ModType.Track;
        if (lower.Contains("skin") || lower.Contains("livery")) return ModType.Skin;
        if (lower.Contains("sound")) return ModType.Sound;
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
        return name;
    }
}
