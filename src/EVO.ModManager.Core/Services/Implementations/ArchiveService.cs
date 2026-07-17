using SharpCompress.Archives.Zip;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ArchiveService : IArchiveService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ArchiveService>();
    private static readonly HashSet<string> SupportedExts = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".7z", ".rar", ".tar", ".tar.gz", ".tgz", ".gz" };

    private static readonly HashSet<string> KspkgExts = new(StringComparer.OrdinalIgnoreCase) { ".kspkg" };
    private static readonly HashSet<string> CardesignExts = new(StringComparer.OrdinalIgnoreCase) { ".cardesign" };

    public bool IsSupportedArchive(string filePath)
    {
        if (SupportedExts.Contains(Path.GetExtension(filePath))) return true;
        if (filePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public ArchiveAnalysisResult AnalyzeArchive(string archivePath)
    {
        var result = new ArchiveAnalysisResult
        {
            ArchiveFormat = Path.GetExtension(archivePath)
        };

        try
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = ZipArchive.OpenArchive(stream);

            var rootDirs = new HashSet<string>();
            var rootFiles = new List<string>();

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;

                var parts = entry.Key?.Split('/', '\\') ?? Array.Empty<string>();

                if (parts.Length == 1)
                    rootFiles.Add(parts[0]);
                else if (parts.Length > 1)
                    rootDirs.Add(parts[0]);

                var ext = Path.GetExtension(entry.Key ?? "");
                if (KspkgExts.Contains(ext))
                {
                    result.HasKspkg = true;
                    result.KspkgFiles.Add(entry.Key ?? "");
                }
                if (CardesignExts.Contains(ext))
                {
                    result.HasCardesign = true;
                    result.CardesignFiles.Add(entry.Key ?? "");
                }

                // AC mod detection
                var keyLower = (entry.Key ?? "").ToLowerInvariant();
                if (keyLower.StartsWith("content/cars/") || keyLower.StartsWith("content/tracks/") ||
                    keyLower.Contains("/content/cars/") || keyLower.Contains("/content/tracks/"))
                {
                    result.IsAcMod = true;
                }
                if (ext.Equals(".kn5", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".kn5") ||
                    keyLower.Contains("ui_car.json") ||
                    keyLower.Contains("ui_track.json"))
                {
                    result.IsAcMod = true;
                }
            }

            result.SuggestedModName = GuessModName(archivePath, rootDirs, rootFiles, result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to analyze archive {Path}", archivePath);
        }

        return result;
    }

    public async Task ExtractArchiveAsync(string archivePath, string destinationDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDir);

        using var stream = File.OpenRead(archivePath);
        using var archive = ZipArchive.OpenArchive(stream);
        var totalEntries = archive.Entries.Count(e => !e.IsDirectory);
        var extracted = 0;

        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveFileTime = true
        };

        using var reader = archive.ExtractAllEntries();
        await Task.Run(() =>
        {
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (!reader.Entry.IsDirectory)
                {
                    reader.WriteEntryToDirectory(destinationDir, options);
                    extracted++;
                    progress?.Report((double)extracted / totalEntries);
                }
            }
        }, ct);

        Log.Information("Extracted {Count} entries from {Archive} to {Dest}",
            extracted, archivePath, destinationDir);
    }

    private static string GuessModName(string archivePath, HashSet<string> rootDirs,
        List<string> rootFiles, ArchiveAnalysisResult result)
    {
        var fileName = Path.GetFileNameWithoutExtension(archivePath);
        if (fileName.EndsWith(".tar")) fileName = Path.GetFileNameWithoutExtension(fileName);

        if (rootDirs.Count == 1 && rootFiles.Count == 0)
            return rootDirs.First();

        if (result.HasKspkg)
            return Path.GetFileNameWithoutExtension(result.KspkgFiles[0]);

        return fileName;
    }
}

