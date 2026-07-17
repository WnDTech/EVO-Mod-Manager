using Serilog;
using System.Linq;
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

        // Output goes to ACE content overlay folder (game reads from both content.kspkg AND content/)
        var aceContentFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "ACE", "content");

        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Information("=== AC-to-ACE Converter (content overlay mode) ===");
            Log.Information("Source: {Source}", sourcePath);

            // Step 1: Extract archive
            progress?.Report(0.1);
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            // Step 2: Analyze mod structure
            progress?.Report(0.3);
            var (modType, modName, contentDir, subDirs) = AnalyzeMod(tempDir, sourcePath);
            Log.Information("  Type: {Type}, Name: {Name}, Subdirs: {Subs}", modType, modName, string.Join(", ", subDirs));

            // Step 3: Copy files to ACE content overlay folder
            progress?.Report(0.5);
            var fileCount = 0;
            foreach (var subDir in subDirs)
            {
                var sourceSubDir = Path.Combine(contentDir, subDir);
                if (!Directory.Exists(sourceSubDir)) continue;

                // Map: AC's content/cars/{carname}/ -> ACE's content/cars/{carname}/
                // The structure is the same for both games
                var targetDir = Path.Combine(aceContentFolder, (modType == "Car" ? "cars" : "tracks"), subDir);
                Directory.CreateDirectory(targetDir);

                foreach (var file in Directory.GetFiles(sourceSubDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(sourceSubDir, file);
                    var dest = Path.Combine(targetDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, true);
                    fileCount++;
                }
            }

            if (fileCount == 0)
            {
                // No specific car/track subdirs found - copy all to mod folder
                var fallbackDir = Path.Combine(aceContentFolder, "cars", modName);
                Directory.CreateDirectory(fallbackDir);
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(tempDir, file);
                    var dest = Path.Combine(fallbackDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, true);
                    fileCount++;
                }
            }

            progress?.Report(0.8);
            Log.Information("  Copied {Count} files to {Path}", fileCount, aceContentFolder);

            // Step 4: Create mod manifest
            var manifest = new SidecarManifest
            {
                Name = modName,
                Type = modType,
                Version = "1.0",
                Author = "AC Converted",
                Description = $"AC mod installed to content overlay: {modName}",
                InstalledAt = DateTime.UtcNow.ToString("O")
            };

            var manifestPath = Path.Combine(aceContentFolder, "cars", modName, ".evomanifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            progress?.Report(1.0);
            Log.Information("Conversion complete: {Name} ({Count} files)", modName, fileCount);

            return new ConversionResult
            {
                Success = true,
                OutputKspkgPath = aceContentFolder,
                ModName = modName,
                ErrorMessage = $"AC mod installed to content overlay: {modName}\n\n" +
                    $"{fileCount} files copied to:\n{aceContentFolder}\n\n" +
                    "The game reads from both content.kspkg and ACE/content/\n" +
                    "Launch ACE EVO to test if the mod works."
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

        // AC mods: content/cars/{carname}/ or content/tracks/{trackname}/
        var contentDir = FindDirectory(dir, "content") ?? dir;
        var carsDir = Path.Combine(contentDir, "cars");
        var tracksDir = Path.Combine(contentDir, "tracks");

        if (Directory.Exists(carsDir))
        {
            var subs = Directory.GetDirectories(carsDir).Select(Path.GetFileName).ToList();
            return ("Car", subs.Count > 0 ? subs[0] : name, carsDir, subs);
        }
        if (Directory.Exists(tracksDir))
        {
            var subs = Directory.GetDirectories(tracksDir).Select(Path.GetFileName).ToList();
            return ("Track", subs.Count > 0 ? subs[0] : name, tracksDir, subs);
        }

        // Fallback: standalone cars/ or tracks/ at any depth
        var anyCars = FindDirectory(dir, "cars");
        if (anyCars != null)
        {
            var subs = Directory.GetDirectories(anyCars).Select(Path.GetFileName).ToList();
            return ("Car", subs.Count > 0 ? subs[0] : name, anyCars, subs);
        }
        var anyTracks = FindDirectory(dir, "tracks");
        if (anyTracks != null)
        {
            var subs = Directory.GetDirectories(anyTracks).Select(Path.GetFileName).ToList();
            return ("Track", subs.Count > 0 ? subs[0] : name, anyTracks, subs);
        }

        // Unknown type - just copy everything
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


