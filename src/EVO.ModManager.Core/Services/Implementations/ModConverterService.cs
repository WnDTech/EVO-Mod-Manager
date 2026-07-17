using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

/// <summary>
/// Converts Assetto Corsa mods to ACE EVO format.
/// Pipeline: Extract -> Identify -> Convert -> Package
/// </summary>
public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();
    private static readonly HttpClient Http = new();

    // Known AC file extensions and their ACE equivalents
    private static readonly HashSet<string> AcModelExts = new(StringComparer.OrdinalIgnoreCase) { ".kn5" };
    private static readonly HashSet<string> AceModelExts = new(StringComparer.OrdinalIgnoreCase) { ".kspkg" };
    private static readonly HashSet<string> TextureExts = new(StringComparer.OrdinalIgnoreCase) { ".dds", ".png", ".jpg", ".tga" };
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".ogg", ".mp3", ".flac" };
    private static readonly HashSet<string> ConfigExts = new(StringComparer.OrdinalIgnoreCase) { ".json", ".ini" };

    public bool IsSdkAvailable => DetectSdkPath() != null;
    public bool IsEvoForgeAvailable => DetectEvoForge() != null;
    public bool CanConvert => IsSdkAvailable || IsEvoForgeAvailable;

    public async Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var modId = Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "convert", modId);
        var aceDir = Path.Combine(outputDir, $"ACE-{modId}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Log.Information("=== AC-to-ACE Conversion Pipeline ===");
            Log.Information("Source: {Source}", sourcePath);

            // Step 1: Extract the archive
            progress?.Report(0.05);
            Log.Information("[1/5] Extracting archive...");
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(sourcePath, tempDir, null, ct);

            // Step 2: Analyze structure
            progress?.Report(0.2);
            Log.Information("[2/5] Analyzing mod structure...");
            var analysis = AnalyzeMod(tempDir, sourcePath);
            Log.Information("  -> Type: {Type}, Name: {Name}", analysis.ModType, analysis.ModName);
            Log.Information("  -> Models: {Models}, Textures: {Texs}, Audio: {Audios}, Configs: {Configs}",
                analysis._ModelCount, analysis.TextureCount, analysis.AudioCount, analysis.ConfigCount);

            // Step 3: Detect format compatibility
            progress?.Report(0.4);
            Log.Information("[3/5] Checking format compatibility...");
            var compat = CheckCompatibility(analysis);

            // Step 4: Convert what we can
            progress?.Report(0.6);
            Log.Information("[4/5] Converting compatible assets...");
            var issues = await ConvertAssetsAsync(tempDir, aceDir, analysis, ct);

            // Step 5: Package for ACE
            progress?.Report(0.8);
            Log.Information("[5/5] Packaging for ACE EVO...");
            var result = await PackageForAceAsync(aceDir, analysis, compat, ct);
            result.ModName = analysis.ModName;

            progress?.Report(1.0);
            return result;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static ModAnalysis AnalyzeMod(string dir, string sourcePath)
    {
        var analysis = new ModAnalysis
        {
            ModName = Path.GetFileNameWithoutExtension(sourcePath)
        };

        // Find content directory (AC mods have content/cars/ or content/tracks/)
        var contentDir = FindDirectory(dir, "content");
        if (contentDir != null)
        {
            var carsDir = Path.Combine(contentDir, "cars");
            var tracksDir = Path.Combine(contentDir, "tracks");

            if (Directory.Exists(carsDir))
            {
                analysis.ModType = "Car";
                analysis.SubDirs = Directory.GetDirectories(carsDir).Select(Path.GetFileName).ToList();
                analysis.RootContentDir = carsDir;
                Log.Information("  Detected car mod with {Count} cars: {Names}",
                    analysis.SubDirs.Count, string.Join(", ", analysis.SubDirs));
            }
            else if (Directory.Exists(tracksDir))
            {
                analysis.ModType = "Track";
                analysis.SubDirs = Directory.GetDirectories(tracksDir).Select(Path.GetFileName).ToList();
                analysis.RootContentDir = tracksDir;
                Log.Information("  Detected track mod: {Name}", analysis.ModName);
            }
            else
            {
                analysis.RootContentDir = contentDir;
            }
        }
        else
        {
            analysis.RootContentDir = dir;
        }

        // Count file types
        var allFiles = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        analysis._ModelCount = allFiles.Count(f => AcModelExts.Contains(Path.GetExtension(f)));
        analysis.TextureCount = allFiles.Count(f => TextureExts.Contains(Path.GetExtension(f)));
        analysis.AudioCount = allFiles.Count(f => AudioExts.Contains(Path.GetExtension(f)));
        analysis.ConfigCount = allFiles.Count(f => ConfigExts.Contains(Path.GetExtension(f)));
        analysis.OtherCount = allFiles.Length - analysis._ModelCount - analysis.TextureCount - analysis.AudioCount - analysis.ConfigCount;

        return analysis;
    }

    private static CompatibilityReport CheckCompatibility(ModAnalysis analysis)
    {
        var report = new CompatibilityReport
        {
            HasModels = analysis._ModelCount > 0,
            HasTextures = analysis.TextureCount > 0,
            HasAudio = analysis.AudioCount > 0,
            HasConfigs = analysis.ConfigCount > 0,
            ModelFormat = analysis._ModelCount > 0 ? ".kn5 (AC native)" : "N/A",
            TargetModelFormat = ".kspkg (ACE native)",
            ModelsConvertible = false, // .kn5 requires reverse engineering or SDK
            TexturesConvertible = analysis.TextureCount > 0, // DDS/PNG can be copied
            AudioConvertible = analysis.AudioCount > 0, // WAV/OGG can be copied
            ConfigsConvertible = analysis.ConfigCount > 0, // JSON can be adapted
            RequiresSdk = analysis._ModelCount > 0 // Models need SDK or external tool
        };

        return report;
    }

    private static async Task<List<ConversionIssue>> ConvertAssetsAsync(string srcDir, string destDir,
        ModAnalysis analysis, CancellationToken ct)
    {
        var issues = new List<ConversionIssue>();

        // Create target structure
        var targetContentDir = Path.Combine(destDir, "content");
        Directory.CreateDirectory(targetContentDir);

        // Copy textures (DDS/PNG/TGA — ACE should read these)
        Log.Information("  Copying {Count} textures...", analysis.TextureCount);
        foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
            .Where(f => TextureExts.Contains(Path.GetExtension(f))))
        {
            ct.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(srcDir, file);
            var destPath = Path.Combine(destDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, true);
        }

        // Copy audio (WAV/OGG — ACE should read these)
        Log.Information("  Copying {Count} audio files...", analysis.AudioCount);
        foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
            .Where(f => AudioExts.Contains(Path.GetExtension(f))))
        {
            ct.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(srcDir, file);
            var destPath = Path.Combine(destDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, true);
        }

        // Copy configs (JSON/INI)
        Log.Information("  Copying {Count} config files...", analysis.ConfigCount);
        foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
            .Where(f => ConfigExts.Contains(Path.GetExtension(f))))
        {
            ct.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(srcDir, file);
            var destPath = Path.Combine(destDir, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, true);
        }

        // Flag model files as needing conversion
        if (analysis._ModelCount > 0)
        {
            issues.Add(new ConversionIssue
            {
                Severity = "Warning",
                Item = $"Models ({analysis._ModelCount} .kn5 files)",
                Detail = ".kn5 models require conversion to ACE format. " +
                    "Launch the ACE Modder to convert these files.",
                Recommendation = "Open ACE Editor → Import model → Export as .kspkg"
            });

            // Copy .kn5 files anyway (for reference in the ACE Editor)
            foreach (var file in Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
                .Where(f => AcModelExts.Contains(Path.GetExtension(f))))
            {
                ct.ThrowIfCancellationRequested();
                var relPath = Path.GetRelativePath(srcDir, file);
                var destPath = Path.Combine(destDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
            }
        }

        return issues;
    }

    private async Task<ConversionResult> PackageForAceAsync(string aceDir, ModAnalysis analysis,
        CompatibilityReport compat, CancellationToken ct)
    {
        // Option 1: SDK Editor available — launch it with the converted files
        var sdkPath = DetectSdkPath();
        if (sdkPath != null)
        {
            var gameDir = DetectGameDir() ?? Path.GetDirectoryName(sdkPath);
            var editorExe = Path.Combine(sdkPath, "AssettoCorsaEVOEditor.exe");

            var psi = new System.Diagnostics.ProcessStartInfo(editorExe)
            {
                WorkingDirectory = gameDir,
                UseShellExecute = true,
                Arguments = $"\"{aceDir}\""
            };
            System.Diagnostics.Process.Start(psi);

            var issues = new List<string>();
            if (compat.HasModels && !compat.ModelsConvertible)
                issues.Add($"{analysis._ModelCount} .kn5 model(s) need conversion via ACE Editor");

            var msg = $"ACE Modder launched with your converted mod files.\n\n" +
                $"Mod: {analysis.ModName}\nType: {analysis.ModType}\n\n" +
                $"Textures ({analysis.TextureCount}) ✓ copied\n" +
                $"Audio ({analysis.AudioCount}) ✓ copied\n" +
                $"Configs ({analysis.ConfigCount}) ✓ copied\n\n" +
                (issues.Count > 0 ? $"⚠ {string.Join("\n⚠ ", issues)}\n\n" : "") +
                $"In the ACE Modder, use File → Export to create a .kspkg file.\n" +
                $"Then drag the .kspkg into EVO Mod Manager.";

            return new ConversionResult { Success = true, ModName = analysis.ModName, ErrorMessage = msg };
        }

        // Option 2: EvoForge available
        var evoForge = DetectEvoForge();
        if (evoForge != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(evoForge)
            {
                Arguments = $"\"{aceDir}\"", UseShellExecute = true
            });

            return new ConversionResult { Success = true, ModName = analysis.ModName,
                ErrorMessage = $"EvoForge launched with {analysis.ModName}. Convert then import the .kspkg." };
        }

        // Option 3: No converter — provide instructions
        return new ConversionResult
        {
            Success = false, ModName = analysis.ModName,
            ErrorMessage = $"Conversion prepared but no converter tool available.\n\n" +
                $"Files are at: {aceDir}\n\n" +
                $"To complete the conversion:\n" +
                $"1. Install ACE Editor SDK (free on Steam)\n" +
                $"2. Open it and import the mod files from the above path\n" +
                $"3. Export as .kspkg\n" +
                $"4. Drag into EVO Mod Manager"
        };
    }

    private static string? FindDirectory(string root, string name)
    {
        return Directory.GetDirectories(root, name, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? DetectSdkPath()
    {
        foreach (var p in new[] {
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK",
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK" })
            if (File.Exists(Path.Combine(p, "AssettoCorsaEVOEditor.exe"))) return p;
        return null;
    }

    private static string? DetectGameDir()
    {
        foreach (var p in new[] {
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO",
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO" })
            if (File.Exists(Path.Combine(p, "AssettoCorsaEVO.exe"))) return p;
        return null;
    }

    private static string? DetectEvoForge()
    {
        foreach (var p in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EvoForge", "EvoForge.exe") })
            if (File.Exists(p)) return p;
        return null;
    }

    private class ModAnalysis
    {
        public string ModName = "";
        public string ModType = "Unknown";
        public string RootContentDir = "";
        public List<string> SubDirs = new();
        public int _ModelCount; public int TextureCount; public int AudioCount; public int ConfigCount; public int OtherCount;
    }

    private class CompatibilityReport
    {
        public bool HasModels, HasTextures, HasAudio, HasConfigs;
        public string ModelFormat = "", TargetModelFormat = "";
        public bool ModelsConvertible, TexturesConvertible, AudioConvertible, ConfigsConvertible;
        public bool RequiresSdk;
    }

    private class ConversionIssue
    {
        public string Severity = "";
        public string Item = "";
        public string Detail = "";
        public string Recommendation = "";
    }
}



