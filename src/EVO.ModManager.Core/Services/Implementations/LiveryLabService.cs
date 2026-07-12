using System.Diagnostics;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class LiveryLabService : ILiveryLabService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LiveryLabService>();
    private const string LiveryLabUrl = "https://liverylab-evo.com/releases/LiveryLab_v1.0.2_Portable.zip";
    private const string ToolsDir = "tools\\LiveryLab";
    private const string ExeName = "LiveryLab.exe";
    private static readonly HttpClient HttpClient = new();

    public string? LiveryLabPath { get; private set; }

    public bool IsInstalled
    {
        get
        {
            LiveryLabPath ??= DetectLiveryLab();
            return LiveryLabPath != null;
        }
    }

    public async Task AutoDownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled) return;

        var toolsBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EVO Mod Manager", ToolsDir);
        Directory.CreateDirectory(toolsBase);

        var zipPath = Path.Combine(toolsBase, "LiveryLab_Portable.zip");

        Log.Information("Downloading LiveryLab from {Url}", LiveryLabUrl);

        using var response = await HttpClient.GetAsync(LiveryLabUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(zipPath);
        await contentStream.CopyToAsync(fileStream, ct);

        using var zipStream = File.OpenRead(zipPath);
        using var archive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(zipStream);
        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory && entry.Key != null)
            {
                var destPath = Path.Combine(toolsBase, entry.Key);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);
                using var entryStream = entry.OpenEntryStream();
                using var destFile = File.Create(destPath);
                await entryStream.CopyToAsync(destFile, ct);
            }
        }

        File.Delete(zipPath);

        LiveryLabPath = DetectLiveryLab();
        Log.Information("LiveryLab installed at {Path}", LiveryLabPath);
    }

    public void LaunchWithZip(string zipPath)
    {
        if (!IsInstalled || LiveryLabPath == null)
        {
            Log.Warning("LiveryLab not available");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo(LiveryLabPath)
            {
                Arguments = $"\"{zipPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            Log.Information("Launched LiveryLab with {Zip}", zipPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch LiveryLab");
        }
    }

    private static string? DetectLiveryLab()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EVO Mod Manager", ToolsDir, ExeName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "LiveryLab", ExeName),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "Local", "LiveryLab", ExeName)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
