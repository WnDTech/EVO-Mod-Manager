using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();

    public bool IsSdkAvailable => DetectSdkPath() != null;
    public bool IsEvoForgeAvailable => false;
    public bool CanConvert => true;

    public async Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var modId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "convert", modId);
        var aceModsFolder = !string.IsNullOrEmpty(outputDir) ? outputDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "ACE", "mods");

        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Information("=== AC-to-ACE Converter (.kspkg) ===");
            Log.Information("Source: {Source}", sourcePath);

            progress?.Report(0.1);
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            progress?.Report(0.3);
            var (modType, modName, contentDir, subDirs) = AnalyzeMod(tempDir, sourcePath);

            progress?.Report(0.5);
            var fileCount = 0;
            var kspkgPath = Path.Combine(aceModsFolder, modName + ".kspkg");

            using (var builder = new KspkgBuilder(kspkgPath))
            {
                foreach (var subDir in subDirs)
                {
                    var srcDir = Path.Combine(contentDir, subDir);
                    if (!Directory.Exists(srcDir)) continue;

                    var prefix = (modType == "Car" ? "content\\cars\\" : "content\\tracks\\") + subDir + "\\";
                    builder.AddDirectory(prefix);

                    foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(srcDir, file).Replace("/", "\\");
                        builder.AddFile(prefix + rel, File.ReadAllBytes(file));
                        fileCount++;
                    }
                }

                if (fileCount == 0)
                {
                    var prefix = "content\\cars\\" + modName + "\\";
                    builder.AddDirectory(prefix);
                    foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = Path.GetRelativePath(tempDir, file).Replace("/", "\\");
                        builder.AddFile(prefix + rel, File.ReadAllBytes(file));
                        fileCount++;
                    }
                }

                builder.Build();
            }

            Log.Information("  Created kspkg: {Path} ({Count} files)", kspkgPath, fileCount);

            // Create manifest
            progress?.Report(0.9);
            var manifest = new SidecarManifest
            {
                Name = modName,
                Type = modType,
                Version = "1.0",
                Author = "AC Converted",
                Description = $"AC mod converted to .kspkg: {modName}",
                InstalledAt = DateTime.UtcNow.ToString("O")
            };
            var manifestPath = Path.Combine(aceModsFolder, modName + ".evomanifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            progress?.Report(1.0);
            return new ConversionResult
            {
                Success = true,
                OutputKspkgPath = kspkgPath,
                ModName = modName,
                ErrorMessage = $"AC mod converted to .kspkg: {modName}\n\n" +
                    $"{fileCount} files packed into:\n{kspkgPath}\n\n" +
                    "Mod installed to ACE mods folder.\n" +
                    "Launch ACE EVO to test."
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Conversion failed for {Source}", sourcePath);
            return new ConversionResult
            {
                Success = false,
                ModName = Path.GetFileNameWithoutExtension(sourcePath),
                ErrorMessage = $"Conversion failed: {ex.Message}"
            };
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static (string modType, string modName, string contentDir, List<string> subDirs) AnalyzeMod(string dir, string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var contentDir = FindDirectory(dir, "content") ?? dir;
        var carsDir = Path.Combine(contentDir, "cars");
        var tracksDir = Path.Combine(contentDir, "tracks");

        if (Directory.Exists(carsDir))
        {
            var subs = Directory.GetDirectories(carsDir).Select(Path.GetFileName).ToList()!;
            return ("Car", subs.Count > 0 ? subs[0] : name, carsDir, subs);
        }
        if (Directory.Exists(tracksDir))
        {
            var subs = Directory.GetDirectories(tracksDir).Select(Path.GetFileName).ToList()!;
            return ("Track", subs.Count > 0 ? subs[0] : name, tracksDir, subs);
        }

        var anyCars = FindDirectory(dir, "cars");
        if (anyCars != null)
        {
            var subs = Directory.GetDirectories(anyCars).Select(Path.GetFileName).ToList()!;
            return ("Car", subs.Count > 0 ? subs[0] : name, anyCars, subs);
        }
        var anyTracks = FindDirectory(dir, "tracks");
        if (anyTracks != null)
        {
            var subs = Directory.GetDirectories(anyTracks).Select(Path.GetFileName).ToList()!;
            return ("Track", subs.Count > 0 ? subs[0] : name, anyTracks, subs);
        }

        return ("Unknown", name, dir, new List<string> { name });
    }

    private static string? FindDirectory(string root, string name)
    {
        return Directory.GetDirectories(root, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? DetectSdkPath()
    {
        foreach (var p in new[] { @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK", @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK" })
            if (File.Exists(Path.Combine(p, "AssettoCorsaEVOEditor.exe"))) return p;
        return null;
    }
}
