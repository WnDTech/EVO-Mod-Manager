using System.ServiceModel.Syndication;
using System.Xml;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModBrowserService : IModBrowserService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModBrowserService>();
    private const string AceModsFeedUrl =
        "https://www.overtake.gg/forums/assetto-corsa-evo-mods.752/-/index.rss?resource=1";
    private static readonly HttpClient HttpClient = new();
    private DateTime _lastFetch = DateTime.MinValue;
    private List<DownloadableMod>? _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    public async Task<List<DownloadableMod>> FetchModListAsync(ModType? category = null,
        CancellationToken ct = default)
    {
        if (_cache != null && DateTime.UtcNow - _lastFetch < CacheTtl)
            return FilterByCategory(_cache, category);

        try
        {
            using var response = await HttpClient.GetAsync(AceModsFeedUrl, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);

            var mods = new List<DownloadableMod>();
            foreach (var item in feed.Items)
            {
                ct.ThrowIfCancellationRequested();

                var downloadUrl = ExtractDownloadUrl(item);

                mods.Add(new DownloadableMod
                {
                    Title = item.Title?.Text ?? "Unknown",
                    Author = item.Authors?.FirstOrDefault()?.Name,
                    Published = item.PublishDate.DateTime,
                    Category = item.Categories?.FirstOrDefault()?.Name,
                    DownloadUrl = downloadUrl,
                    ResourceId = ExtractResourceId(downloadUrl),
                    Description = StripHtml(item.Content?.ToString() ?? item.Summary?.Text ?? ""),
                    ModType = ClassifyFromCategory(item.Categories?.FirstOrDefault()?.Name)
                });
            }

            _cache = mods;
            _lastFetch = DateTime.UtcNow;
            Log.Information("Fetched {Count} mods from OverTake.gg", mods.Count);

            return FilterByCategory(mods, category);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch mod list from OverTake.gg");
            return _cache ?? new List<DownloadableMod>();
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
        return categoryName?.ToLowerInvariant() switch
        {
            var c when c?.Contains("car") == true => ModType.Car,
            var c when c?.Contains("track") == true => ModType.Track,
            var c when c?.Contains("skin") == true => ModType.Skin,
            var c when c?.Contains("sound") == true => ModType.Sound,
            var c when c?.Contains("app") == true => ModType.App,
            _ => ModType.Misc
        };
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
