using System.Diagnostics;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();

    public bool IsSdkAvailable => DetectSdkPath() != null;
    public bool IsEvoForgeAvailable => DetectEvoForge() != null;
    public bool CanConvert => IsSdkAvailable || IsEvoForgeAvailable;

    public Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        // Priority 1: EvoForge (dedicated AC-to-ACE converter)
        var evoForgePath = DetectEvoForge();
        if (evoForgePath != null)
        {
            try
            {
                var psi = new ProcessStartInfo(evoForgePath)
                {
                    Arguments = $"\"{sourcePath}\"",
                    WorkingDirectory = Path.GetDirectoryName(evoForgePath),
                    UseShellExecute = true
                };
                Process.Start(psi);
                Log.Information("Launched EvoForge to convert {Source}", sourcePath);

                return Task.FromResult(new ConversionResult
                {
                    Success = true,
                    ModName = Path.GetFileNameWithoutExtension(sourcePath),
                    ErrorMessage = "EvoForge has been launched with your AC mod. " +
                        "Convert it through EvoForge, then drag the resulting .kspkg file into EVO Mod Manager."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch EvoForge");
            }
        }

        // Priority 2: ACE Editor SDK
        var sdkPath = DetectSdkPath();
        if (sdkPath != null)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(sdkPath, "AssettoCorsaEVOEditor.exe"))
                {
                    Arguments = $"\"{sourcePath}\"",
                    WorkingDirectory = sdkPath,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Log.Information("Launched ACE Editor with {Source}", sourcePath);

                return Task.FromResult(new ConversionResult
                {
                    Success = true,
                    ModName = Path.GetFileNameWithoutExtension(sourcePath),
                    ErrorMessage = "ACE Editor has been opened with your AC mod. " +
                        "Use the editor to export/pack it for ACE EVO, then drag the .kspkg into EVO Mod Manager."
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch ACE Editor");
            }
        }

        // No converter available
        Log.Warning("No AC-to-ACE converter available for {Source}", sourcePath);
        return Task.FromResult(new ConversionResult
        {
            Success = false,
            ErrorMessage = "No converter available.\n\n" +
                "To convert AC mods to ACE EVO format:\n" +
                "1. Install EvoForge (recommended) or\n" +
                "2. Use the ACE Editor SDK to repackage the mod\n\n" +
                "Once converted, drag the .kspkg file into EVO Mod Manager."
        });
    }

    private static string? DetectSdkPath()
    {
        var possiblePaths = new[]
        {
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK",
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(Path.Combine(path, "AssettoCorsaEVOEditor.exe")))
                return path;
        }
        return null;
    }

    private static string? DetectEvoForge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EvoForge", "EvoForge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Programs", "EvoForge", "EvoForge.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
