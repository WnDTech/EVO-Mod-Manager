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
    private static bool _isDownloading;

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
        if (_isDownloading)
        {
            Log.Warning("LiveryLab download already in progress");
            return;
        }

        _isDownloading = true;
        try
        {
            var toolsBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EVO Mod Manager", ToolsDir);
            Directory.CreateDirectory(toolsBase);

            Log.Information("Downloading LiveryLab from {Url}", LiveryLabUrl);

            using var response = await HttpClient.GetAsync(LiveryLabUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            // Extract directly from the HTTP stream without saving to a temp zip
            using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            using var archive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(responseStream);

            var totalEntries = archive.Entries.Count(e => !e.IsDirectory);
            var extracted = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory || entry.Key == null) continue;

                var destPath = Path.Combine(toolsBase, entry.Key);
                var destDir = Path.GetDirectoryName(destPath);
                if (destDir != null) Directory.CreateDirectory(destDir);

                using var entryStream = entry.OpenEntryStream();
                using var destFile = File.Create(destPath);
                await entryStream.CopyToAsync(destFile, ct);
                extracted++;
                progress?.Report((double)extracted / totalEntries);
            }

            LiveryLabPath = DetectLiveryLab();
            Log.Information("LiveryLab installed at {Path} ({Count} files)", LiveryLabPath, extracted);
        }
        finally
        {
            _isDownloading = false;
        }
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
