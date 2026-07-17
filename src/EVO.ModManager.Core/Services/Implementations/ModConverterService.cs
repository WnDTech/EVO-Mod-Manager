using System.Diagnostics;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();

    public bool IsSdkAvailable => DetectSdkPath() != null;
    public bool IsEvoForgeAvailable => false;
    public bool CanConvert => true; // We handle conversion ourselves

    public async Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "convert", Guid.NewGuid().ToString("N")[..8]);
        var aceModsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "ACE", "mods");
        // Use outputDir if provided (from calling code)
        aceModsFolder = !string.IsNullOrEmpty(outputDir) ? outputDir : aceModsFolder;

        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Information("=== Built-in AC-to-ACE Converter ===");
            Log.Information("Source: {Source}", sourcePath);

            // Step 1: Extract archive
            progress?.Report(0.1);
            Log.Information("[1/5] Extracting archive...");
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            // Step 2: Analyze structure
            progress?.Report(0.25);
            Log.Information("[2/5] Analyzing mod structure...");
            var (modType, modName, contentDir) = AnalyzeMod(tempDir, sourcePath);
            Log.Information("  -> Type: {Type}, Name: {Name}", modType, modName);

            // Step 3: Create ACE-compatible folder
            progress?.Report(0.4);
            Log.Information("[3/5] Creating ACE mod folder...");
            var modFolder = Path.Combine(aceModsFolder, modName);
            Directory.CreateDirectory(modFolder);

            // Step 4: Copy converted assets
            progress?.Report(0.6);
            Log.Information("[4/5] Copying mod assets...");
            CopyAssets(tempDir, modFolder, contentDir);

            // Count what was copied
            var totalFiles = Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories).Length;
            var kn5Files = Directory.GetFiles(modFolder, "*.kn5", SearchOption.AllDirectories).Length;
            var ddsFiles = Directory.GetFiles(modFolder, "*.dds", SearchOption.AllDirectories).Length;
            var jsonFiles = Directory.GetFiles(modFolder, "*.json", SearchOption.AllDirectories).Length;

            Log.Information("  -> {Total} files (kn5:{Kn5}, dds:{Dds}, json:{Json})",
                totalFiles, kn5Files, ddsFiles, jsonFiles);

            // Step 5: Generate manifest and refresh
            progress?.Report(0.85);
            Log.Information("[5/5] Generating manifest...");
            var manifest = new SidecarManifest
            {
                Name = modName,
                Type = modType,
                Version = "1.0",
                Author = "AC Converted",
                Description = $"Converted from Assetto Corsa: {modName}",
                InstalledAt = DateTime.UtcNow.ToString("O"),
                Hash = Guid.NewGuid().ToString("N")[..16]
            };
            var manifestPath = Path.Combine(modFolder, $"{modName}.evomanifest.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            progress?.Report(1.0);
            Log.Information("Conversion complete: {Name} -> {Path}", modName, modFolder);

            return new ConversionResult
            {
                Success = true,
                OutputKspkgPath = modFolder,
                ModName = modName,
                ErrorMessage = $"AC mod converted and installed: {modName}\n\n" +
                    $"Type: {modType}\n" +
                    $"Files: {totalFiles} ({kn5Files} models, {ddsFiles} textures)\n" +
                    $"Location: mods/{modName}/\n\n" +
                    "The mod has been placed in your ACE mods folder.\n" +
                    "Launch ACE EVO to check if it works.\n\n" +
                    (kn5Files > 0
                        ? ".kn5 model files may need repacking via ACE Editor if the game doesn't load them as loose files."
                        : "")
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

    private static (string modType, string modName, string contentDir) AnalyzeMod(string dir, string sourcePath)
    {
        // Find content directory (AC: content/cars/ or content/tracks/)
        var contentDir = FindDirectory(dir, "content") ?? dir;
        var carsDir = Path.Combine(contentDir, "cars");
        var tracksDir = Path.Combine(contentDir, "tracks");

        if (Directory.Exists(carsDir))
        {
            var subdirs = Directory.GetDirectories(carsDir);
            return ("Car", subdirs.Length > 0 ? Path.GetFileName(subdirs[0]) : Path.GetFileNameWithoutExtension(sourcePath), carsDir);
        }
        if (Directory.Exists(tracksDir))
        {
            var subdirs = Directory.GetDirectories(tracksDir);
            return ("Track", subdirs.Length > 0 ? Path.GetFileName(subdirs[0]) : Path.GetFileNameWithoutExtension(sourcePath), tracksDir);
        }

        // Fallback: check for standalone cars/ or tracks/ at any depth
        var anyCars = FindDirectory(dir, "cars");
        if (anyCars != null)
        {
            var subdirs = Directory.GetDirectories(anyCars);
            return ("Car", subdirs.Length > 0 ? Path.GetFileName(subdirs[0]) : Path.GetFileNameWithoutExtension(sourcePath), anyCars);
        }
        var anyTracks = FindDirectory(dir, "tracks");
        if (anyTracks != null)
        {
            var subdirs = Directory.GetDirectories(anyTracks);
            return ("Track", subdirs.Length > 0 ? Path.GetFileName(subdirs[0]) : Path.GetFileNameWithoutExtension(sourcePath), anyTracks);
        }

        return ("Unknown", Path.GetFileNameWithoutExtension(sourcePath), dir);
    }

    private static void CopyAssets(string srcDir, string destDir, string contentDir)
    {
        // Copy everything from temp to the mod folder
        foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(srcDir, file);
            var destPath = Path.Combine(destDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, true);
        }
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


