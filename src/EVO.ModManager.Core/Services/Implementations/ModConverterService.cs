using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ModConverterService : IModConverterService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ModConverterService>();

    public bool IsSdkAvailable
    {
        get
        {
            var sdkPath = DetectSdkPath();
            return sdkPath != null;
        }
    }

    public Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var sdkPath = DetectSdkPath();
        if (sdkPath == null)
        {
            return Task.FromResult(new ConversionResult
            {
                Success = false,
                ErrorMessage = "ACE Editor SDK not found. Install or locate it in Settings."
            });
        }

        // Phase 1 stub: converter will be fully implemented in Phase 5
        Log.Information("Converter stub: would convert {Source} -> {Output} using SDK at {Sdk}",
            sourcePath, outputDir, sdkPath);

        return Task.FromResult(new ConversionResult
        {
            Success = false,
            ErrorMessage = "Converter not yet implemented (Phase 5)"
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
}
