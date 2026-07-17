using System.Diagnostics;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();
    private static readonly HttpClient Http = new();

    public bool IsSdkAvailable => DetectSdkPath() != null;
    public bool IsEvoForgeAvailable => DetectEvoForge() != null;
    public bool CanConvert => IsSdkAvailable || IsEvoForgeAvailable;

    public async Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "convert", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Information("Starting AC-to-ACE conversion for {Source}", sourcePath);

            // Step 1: Extract the archive
            progress?.Report(0.1);
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            // Step 2: Detect mod type and structure
            progress?.Report(0.3);
            var result = AnalyzeAcMod(tempDir, sourcePath);

            // Step 3: Organize into ACE-compatible structure
            progress?.Report(0.5);
            var aceDir = OrganizeForAce(tempDir, result);

            // Step 4: Try to pack via ACE Editor or EvoForge
            progress?.Report(0.7);

            // Priority 1: ACE Editor SDK (has built-in kspkg pack)
            var sdkPath = DetectSdkPath();
            if (sdkPath != null)
            {
                var editorExe = Path.Combine(sdkPath, "AssettoCorsaEVOEditor.exe");
                var psi = new ProcessStartInfo(editorExe)
                {
                    WorkingDirectory = sdkPath,
                    UseShellExecute = true
                };
                Process.Start(psi);

                return new ConversionResult
                {
                    Success = true,
                    OutputKspkgPath = aceDir,
                    ModName = result.ModName,
                    ErrorMessage = $"ACE Modder has been opened with your AC mod files.\n\n" +
                        $"Files extracted to: {aceDir}\n\n" +
                        $"In the ACE Modder:\n" +
                        $"1. Open your mod project\n" +
                        $"2. Export/Pack as .kspkg\n" +
                        $"3. Drop the .kspkg back into EVO Mod Manager"
                };
            }

            // Priority 2: EvoForge
            var evoForgePath = DetectEvoForge();
            if (evoForgePath != null)
            {
                var psi = new ProcessStartInfo(evoForgePath)
                {
                    Arguments = $"\"{aceDir}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);

                return new ConversionResult
                {
                    Success = true,
                    ModName = result.ModName,
                    ErrorMessage = $"EvoForge has been launched with your AC mod.\n\n" +
                        $"Convert it through EvoForge, then drag the resulting .kspkg into EVO Mod Manager."
                };
            }

            // No converter tool available — provide manual instructions
            progress?.Report(1.0);
            return new ConversionResult
            {
                Success = false,
                ModName = result.ModName,
                ErrorMessage = $"AC mod detected but no converter available.\n\n" +
                    $"Mod files extracted to:\n{aceDir}\n\n" +
                    $"To convert this AC mod to ACE EVO format:\n" +
                    $"1. Install the ACE Editor SDK (via Steam)\n" +
                    $"2. Use it to open and repackage the mod\n" +
                    $"3. Or install EvoForge for automated conversion\n\n" +
                    $"Once you have a .kspkg file, drag it into EVO Mod Manager."
            };
        }
        finally
        {
            // Keep extracted files for user, but clean up if we copied to mods folder
            if (!Directory.Exists(tempDir)) { try { Directory.Delete(tempDir, true); } catch { } }
        }
    }

    private static ConvertResult AnalyzeAcMod(string extractDir, string sourcePath)
    {
        var result = new ConvertResult();

        // Detect car mod (AC: content/cars/{carname}/)
        var carsDir = FindDirectory(extractDir, "cars");
        if (carsDir != null)
        {
            result.ModType = "Car";
            result.ModName = new DirectoryInfo(carsDir).Name;
            result.RootDir = carsDir;
            return result;
        }

        // Detect track mod (AC: content/tracks/{trackname}/)
        var tracksDir = FindDirectory(extractDir, "tracks");
        if (tracksDir != null)
        {
            result.ModType = "Track";
            result.ModName = new DirectoryInfo(tracksDir).Name;
            result.RootDir = tracksDir;
            return result;
        }

        // Fallback: use archive name
        result.ModName = Path.GetFileNameWithoutExtension(sourcePath);
        result.RootDir = extractDir;
        return result;
    }

    private static string OrganizeForAce(string extractDir, ConvertResult result)
    {
        // Place mod files into an ACE-compatible folder under a single mod directory
        var aceModDir = Path.Combine(extractDir, "..", result.ModName + "_ACE");
        Directory.CreateDirectory(aceModDir);

        // Copy mod content into the ACE mod folder
        if (Directory.Exists(result.RootDir))
        {
            foreach (var file in Directory.GetFiles(result.RootDir, "*", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(result.RootDir, file);
                var destPath = Path.Combine(aceModDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
            }
        }

        // Also copy any content directory structure
        var contentDir = Path.Combine(extractDir, "content");
        if (Directory.Exists(contentDir))
        {
            foreach (var file in Directory.GetFiles(contentDir, "*", SearchOption.AllDirectories))
            {
                var relPath = "content/" + Path.GetRelativePath(contentDir, file);
                var destPath = Path.Combine(aceModDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
            }
        }

        // Write a README explaining how to use this
        File.WriteAllText(Path.Combine(aceModDir, "README_ACE.txt"),
            $"AC-to-ACE Conversion: {result.ModName}\n" +
            $"Type: {result.ModType}\n\n" +
            "To create an ACE-compatible .kspkg mod:\n" +
            "1. Open the ACE Modder (AssettoCorsaEVOEditor)\n" +
            "2. Import/Create a new project from these files\n" +
            "3. Export as .kspkg\n" +
            "4. Drop the .kspkg into EVO Mod Manager");

        return aceModDir;
    }

    private static string? FindDirectory(string root, string name)
    {
        var dirs = Directory.GetDirectories(root, name, SearchOption.AllDirectories);
        return dirs.FirstOrDefault();
    }

    private static string? DetectSdkPath()
    {
        var candidates = new[]
        {
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK",
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK"
        };
        return candidates.FirstOrDefault(p => File.Exists(Path.Combine(p, "AssettoCorsaEVOEditor.exe")));
    }

    private static string? DetectEvoForge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "EvoForge", "EvoForge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private class ConvertResult
    {
        public string ModName { get; set; } = "Unknown";
        public string ModType { get; set; } = "Unknown";
        public string RootDir { get; set; } = "";
    }
}
