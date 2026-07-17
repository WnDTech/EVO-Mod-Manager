using System.IO.Compression;
using System.Text.Json;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class CollectionService : ICollectionService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<CollectionService>();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task ExportCollectionAsync(IEnumerable<Mod> mods, string destinationPath)
    {
        var collection = new CollectionFile
        {
            AppVersion = "1.0.0",
            ExportedAt = DateTime.UtcNow,
            Mods = mods.Select(m => new CollectionModEntry
            {
                Name = m.Name,
                ModType = m.ModType.ToString(),
                Version = m.Version,
                Author = m.Author,
                SourceUrl = m.SourceUrl,
                IsEnabled = m.IsEnabled
            }).ToList()
        };

        var json = JsonSerializer.Serialize(collection, JsonOptions);
        File.WriteAllText(destinationPath, json);

        Log.Information("Exported collection ({Count} mods) to {Path}", collection.Mods.Count, destinationPath);
        return Task.CompletedTask;
    }

    public Task<CollectionResult> ImportCollectionAsync(string sourcePath, string modsFolder)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Collection file not found", sourcePath);

        var json = File.ReadAllText(sourcePath);
        var collection = JsonSerializer.Deserialize<CollectionFile>(json)
            ?? throw new InvalidDataException("Failed to parse collection file");

        var installedMods = new HashSet<string>(
            Directory.EnumerateFileSystemEntries(modsFolder)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(x => x != null)!,
            StringComparer.OrdinalIgnoreCase);

        var result = new CollectionResult();

        foreach (var entry in collection.Mods)
        {
            if (installedMods.Contains(entry.Name))
            {
                result.AlreadyInstalled.Add(entry);
            }
            else
            {
                result.ImportedMods.Add(entry);
            }
        }

        Log.Information("Imported collection: {New} new, {Existing} already installed from {Path}",
            result.ImportedMods.Count, result.AlreadyInstalled.Count, sourcePath);
        return Task.FromResult(result);
    }

    public async Task ExportCollectionWithFilesAsync(IEnumerable<Mod> mods, string destinationZip, string modsFolder)
    {
        var modList = mods.ToList();
        var collection = new CollectionFile
        {
            AppVersion = "1.0.0",
            ExportedAt = DateTime.UtcNow,
            Mods = modList.Select(m => new CollectionModEntry
            {
                Name = m.Name,
                ModType = m.ModType.ToString(),
                Version = m.Version,
                Author = m.Author,
                SourceUrl = m.SourceUrl,
                IsEnabled = m.IsEnabled
            }).ToList()
        };

        var manifestJson = JsonSerializer.Serialize(collection, JsonOptions);

        using var stream = File.Create(destinationZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry("collection.json");
        await using (var writer = manifestEntry.Open())
        await using (var sw = new StreamWriter(writer))
        {
            await sw.WriteAsync(manifestJson);
        }

        foreach (var mod in modList)
        {
            var filePath = mod.IsEnabled
                ? Path.Combine(modsFolder, mod.FileName)
                : mod.DisabledPath;

            if (filePath == null || !File.Exists(filePath))
            {
                Log.Warning("Mod file not found for collection export: {Name} at {Path}", mod.Name, filePath);
                continue;
            }

            var entry = archive.CreateEntry(mod.FileName, CompressionLevel.Optimal);
            await using var fileStream = File.OpenRead(filePath);
            await using var entryStream = entry.Open();
            await fileStream.CopyToAsync(entryStream);

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
                await scStream.CopyToAsync(scEntryStream);
            }
        }

        Log.Information("Exported collection with files ({Count} mods) to {Path}", modList.Count, destinationZip);
    }

    public async Task ImportCollectionWithFilesAsync(string sourceZip, string destinationFolder, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(destinationFolder);

        using var stream = File.OpenRead(sourceZip);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
        var total = entries.Count;
        var processed = 0;

        foreach (var entry in entries)
        {
            var destPath = Path.Combine(destinationFolder, entry.FullName);
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null) Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);

            processed++;
            progress?.Report((double)processed / total);
        }

        Log.Information("Imported collection with files ({Count} entries) from {Zip} to {Dest}",
            processed, sourceZip, destinationFolder);
    }
}
