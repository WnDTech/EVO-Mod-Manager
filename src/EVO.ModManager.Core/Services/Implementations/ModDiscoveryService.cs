using System.Security.Cryptography;
using System.Linq;
using System.Text.Json;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModDiscoveryService : IModDiscoveryService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModDiscoveryService>();

    public Task<List<Mod>> ScanModsFolderAsync(string modsFolder, CancellationToken ct = default)
    {
        var mods = new List<Mod>();

        if (!Directory.Exists(modsFolder))
        {
            Log.Warning("Mods folder does not exist: {Path}", modsFolder);
            return Task.FromResult(mods);
        }

        // 1. Scan flat .kspkg files in root
        foreach (var filePath in Directory.EnumerateFiles(modsFolder, "*.kspkg", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var mod = ProcessKspkgFile(filePath);
            if (mod != null) mods.Add(mod);
        }

        // 2. Scan subdirectories for .kspkg files AND loose-file mods
        foreach (var dir in Directory.EnumerateDirectories(modsFolder))
        {
            ct.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith("_disabled")) continue;

            // Check for .kspkg files inside subdirectory
            var kspkgFiles = Directory.GetFiles(dir, "*.kspkg", SearchOption.TopDirectoryOnly);
            foreach (var kspkgPath in kspkgFiles)
            {
                var mod = ProcessKspkgFile(kspkgPath);
                if (mod != null) mods.Add(mod);
            }

            // Also detect loose-file mods (converted AC mods with manifest)
            if (kspkgFiles.Length == 0)
            {
                var manifestFiles = Directory.GetFiles(dir, "*.evomanifest.json", SearchOption.TopDirectoryOnly);
                if (manifestFiles.Length > 0)
                {
                    foreach (var mf in manifestFiles)
                    {
                        var mod = ProcessManifestMod(dir, mf);
                        if (mod != null) mods.Add(mod);
                    }
                }
                else if (Directory.GetFiles(dir, "*.kn5", SearchOption.AllDirectories).Length > 0)
                {
                    // Fallback: detect as unknown mod with kn5 files
                    mods.Add(new Mod
                    {
                        Name = dirName,
                        FileName = $"{dirName} (loose files)",
                        ModType = ModType.Misc,
                        SizeBytes = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length),
                        InstalledAt = Directory.GetCreationTimeUtc(dir),
                        IsEnabled = true,
                        Description = "AC mod installed via converter"
                    });
                }
            }
        }

        Log.Information("Scanned {Count} mods from {Path}", mods.Count, modsFolder);
        return Task.FromResult(mods);
    }

    private Mod? ProcessManifestMod(string modDir, string manifestPath)
    {
        try
        {
            var manifest = ReadSidecarManifest(manifestPath);
            if (manifest == null) return null;

            var totalSize = Directory.GetFiles(modDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);

            return new Mod
            {
                Id = manifest.Id,
                Name = manifest.Name,
                FileName = Path.GetFileName(modDir),
                ModType = Enum.TryParse<ModType>(manifest.Type, out var t) ? t : ModType.Misc,
                Version = manifest.Version,
                Author = manifest.Author,
                Description = manifest.Description,
                SizeBytes = totalSize,
                InstalledAt = DateTime.Parse(manifest.InstalledAt),
                SourceUrl = manifest.SourceUrl,
                SourceModId = manifest.SourceModId,
                IsEnabled = true,
                IsSymlinked = false
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process manifest mod at {Path}", manifestPath);
            return null;
        }
    }

    public Task<Mod?> GetModFromKspkgAsync(string kspkgPath, CancellationToken ct = default)
    {
        return Task.FromResult(ProcessKspkgFile(kspkgPath));
    }

    public SidecarManifest? ReadSidecarManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return null;
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<SidecarManifest>(json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read sidecar manifest at {Path}", manifestPath);
            return null;
        }
    }

    public void WriteSidecarManifest(string manifestPath, SidecarManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    public ModType ClassifyMod(string modName, string? sourceUrl)
    {
        var lower = modName.ToLowerInvariant();

        if (sourceUrl?.Contains("/ace-cars.") == true) return ModType.Car;
        if (sourceUrl?.Contains("/ace-skins.") == true) return ModType.Skin;
        if (sourceUrl?.Contains("/ace-apps.") == true) return ModType.App;
        if (sourceUrl?.Contains("/ace-tracks.") == true) return ModType.Track;
        if (sourceUrl?.Contains("/ace-sounds.") == true) return ModType.Sound;
        if (sourceUrl?.Contains("/ace-misc.") == true) return ModType.Misc;

        if (lower.Contains("car") || lower.Contains("vehicle")) return ModType.Car;
        if (lower.Contains("track") || lower.Contains("circuit") || lower.Contains("ring")) return ModType.Track;
        if (lower.Contains("skin") || lower.Contains("livery") || lower.Contains("paint")) return ModType.Skin;
        if (lower.Contains("sound") || lower.Contains("audio") || lower.Contains("engine")) return ModType.Sound;
        if (lower.Contains("app") || lower.Contains("plugin") || lower.Contains("tool")) return ModType.App;

        return ModType.Misc;
    }

    private Mod? ProcessKspkgFile(string kspkgPath)
    {
        try
        {
            var fi = new FileInfo(kspkgPath);
            if (!fi.Exists) return null;

            var fileName = fi.Name;
            var modName = Path.GetFileNameWithoutExtension(fileName);
            var dir = fi.DirectoryName!;
            var sidecarPath = Path.Combine(dir, $"{modName}.evomanifest.json");

            var manifest = ReadSidecarManifest(sidecarPath);

            using var sha256 = SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(File.ReadAllBytes(kspkgPath)));

            var mod = new Mod
            {
                Name = manifest?.Name ?? modName,
                FileName = fileName,
                ModType = manifest != null
                    ? (Enum.TryParse<ModType>(manifest.Type, out var t) ? t : ModType.Unknown)
                    : ClassifyMod(modName, manifest?.SourceUrl),
                Version = manifest?.Version,
                Author = manifest?.Author,
                Description = manifest?.Description,
                SizeBytes = fi.Length,
                InstalledAt = manifest != null ? DateTime.Parse(manifest.InstalledAt) : fi.CreationTimeUtc,
                SourceUrl = manifest?.SourceUrl,
                SourceModId = manifest?.SourceModId,
                Hash = hash,
                IsSymlinked = (fi.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint,
                SymlinkTarget = ResolveSymlinkTarget(kspkgPath)
            };

            if (manifest != null && Guid.TryParse(manifest.Id, out var guid))
                mod.Id = manifest.Id;

            return mod;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process kspkg file {Path}", kspkgPath);
            return null;
        }
    }

    private static string? ResolveSymlinkTarget(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                return di.LinkTarget;
        }
        catch { }
        return null;
    }
}



