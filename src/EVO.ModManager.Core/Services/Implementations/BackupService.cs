using System.IO.Compression;
using System.Text.Json;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class BackupService : IBackupService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<BackupService>();

    public async Task BackupFullAsync(string modsFolder, string destinationZip,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var modFiles = Directory.GetFiles(modsFolder, "*.kspkg", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_disabled"))
            .ToList();

        var manifest = modFiles.Select(f => new FileInfo(f)).Select(fi => new
        {
            FileName = fi.Name,
            RelativePath = Path.GetRelativePath(modsFolder, fi.FullName),
            Size = fi.Length,
            LastWrite = fi.LastWriteTimeUtc.ToString("O")
        }).ToList();

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        using var stream = File.Create(destinationZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry("backup_manifest.json");
        await using (var writer = manifestEntry.Open())
        await using (var sw = new StreamWriter(writer))
        {
            await sw.WriteAsync(manifestJson);
        }

        var totalFiles = modFiles.Count;
        var processed = 0;

        foreach (var filePath in modFiles)
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(modsFolder, filePath);
            var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);

            await using var fileStream = File.OpenRead(filePath);
            await using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream, ct);

            processed++;
            progress?.Report((double)processed / totalFiles);
        }

        Log.Information("Full backup created: {Count} files -> {Dest}", processed, destinationZip);
    }

    public async Task BackupSelectedAsync(string modsFolder, string destinationZip, List<Mod> mods,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var stream = File.Create(destinationZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var totalFiles = mods.Count;
        var processed = 0;

        foreach (var mod in mods)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = mod.IsEnabled
                ? Path.Combine(modsFolder, mod.FileName)
                : mod.DisabledPath;

            if (filePath == null || !File.Exists(filePath)) continue;

            var entry = archive.CreateEntry(mod.FileName, CompressionLevel.Optimal);
            await using var fileStream = File.OpenRead(filePath);
            await using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream, ct);

            // Also include sidecar if it exists
            var sidecarPath = Path.Combine(
                Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileNameWithoutExtension(mod.FileName)}.evomanifest.json");
            if (File.Exists(sidecarPath))
            {
                var scEntry = archive.CreateEntry(
                    $"{Path.GetFileNameWithoutExtension(mod.FileName)}.evomanifest.json",
                    CompressionLevel.Optimal);
                await using var scStream = File.OpenRead(sidecarPath);
                await using var scEntryStream = scEntry.Open();
                await scStream.CopyToAsync(scEntryStream, ct);
            }

            processed++;
            progress?.Report((double)processed / totalFiles);
        }

        Log.Information("Selective backup created: {Count} mods -> {Dest}", processed, destinationZip);
    }

    public Task<List<Mod>> PreviewBackupAsync(string backupZip)
    {
        var mods = new List<Mod>();

        using var stream = File.OpenRead(backupZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.Name.EndsWith(".kspkg", StringComparison.OrdinalIgnoreCase))
            {
                mods.Add(new Mod
                {
                    Name = Path.GetFileNameWithoutExtension(entry.Name),
                    FileName = entry.Name,
                    SizeBytes = entry.Length,
                    InstalledAt = entry.LastWriteTime.DateTime
                });
            }
        }

        return Task.FromResult(mods);
    }

    public async Task RestoreAsync(string backupZip, string modsFolder, List<string>? modIdsToRestore = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var stream = File.OpenRead(backupZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = archive.Entries
            .Where(e => !e.FullName.EndsWith('/'))
            .ToList();

        var total = entries.Count;
        var processed = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var destPath = Path.Combine(modsFolder, entry.FullName);
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null) Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);

            processed++;
            progress?.Report((double)processed / total);
        }

        Log.Information("Backup restored: {Count} files -> {Dest}", processed, modsFolder);
    }
}
