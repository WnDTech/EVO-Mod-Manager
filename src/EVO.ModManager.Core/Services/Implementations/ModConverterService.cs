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
            Log.Information("=== AC-to-ACE Converter ===");
            Log.Information("Source: {Source}", sourcePath);

            // Step 1: Extract archive
            progress?.Report(0.1);
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            // Step 2: Analyze structure
            progress?.Report(0.3);
            var (modType, modName, contentDir) = AnalyzeMod(tempDir, sourcePath);

            // Step 3: Copy to ACE-Modder Editor folder (so Editor can see it)
            progress?.Report(0.5);
            var editorModsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Saved Games", "ACE-Modder", "mods", modName);
            Directory.CreateDirectory(editorModsFolder);

            foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(tempDir, file);
                var destPath = Path.Combine(editorModsFolder, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
            }

            // Step 4: Copy a manifest to the mods folder for tracking
            progress?.Report(0.7);
            var manifestPath = Path.Combine(aceModsFolder, $"{modName}.evomanifest.json");
            var manifest = new SidecarManifest
            {
                Name = modName,
                Type = modType,
                Version = "1.0",
                Author = "AC Converted",
                Description = $"AC mod extracted. Use ACE Editor to export as .kspkg",
                InstalledAt = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Step 5: Launch ACE Editor SDK from game directory
            progress?.Report(0.9);
            var sdkPath = DetectSdkPath();
            var gameDir = DetectGameDir();

            if (sdkPath != null && gameDir != null)
            {
                var editorExe = Path.Combine(sdkPath, "AssettoCorsaEVOEditor.exe");
                var psi = new ProcessStartInfo(editorExe)
                {
                    WorkingDirectory = gameDir,
                    UseShellExecute = true,
                    Arguments = $"\"{editorModsFolder}\""
                };
                Process.Start(psi);
            }

            progress?.Report(1.0);
            Log.Information("Conversion prepared: {Name}", modName);

            return new ConversionResult
            {
                Success = true,
                OutputKspkgPath = editorModsFolder,
                ModName = modName,
                ErrorMessage = $"AC mod prepared: {modName}\n\n" +
                    $"Type: {modType}\n\n" +
                    "ACE EVO only loads .kspkg mod files.\n" +
                    "ACE Modder has been opened with your mod files.\n\n" +
                    "In the ACE Modder:\n" +
                    "1. Open your mod from the project panel\n" +
                    "2. Export/Pack as .kspkg\n" +
                    "3. Drop the .kspkg file into EVO Mod Manager\n\n" +
                    "Files also copied to:\n" +
                    $"{editorModsFolder}"
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

    private static string? DetectGameDir()
    {
        foreach (var p in new[] { @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO", @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO" })
            if (File.Exists(Path.Combine(p, "AssettoCorsaEVO.exe"))) return p;
        return null;
    }
}
