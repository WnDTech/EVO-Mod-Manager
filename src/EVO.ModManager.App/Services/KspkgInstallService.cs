using System.IO;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.Services;

public class KspkgInstallService
{
    private readonly IGameDetectionService _gameDetection;
    private readonly IModDiscoveryService _modDiscovery;
    private static readonly ILogger Log = Serilog.Log.ForContext<KspkgInstallService>();

    public KspkgInstallService(IGameDetectionService gameDetection, IModDiscoveryService modDiscovery)
    {
        _gameDetection = gameDetection;
        _modDiscovery = modDiscovery;
    }

    public Task<string> InstallAsync(string kspkgPath, CancellationToken ct = default)
    {
        if (!File.Exists(kspkgPath))
            throw new FileNotFoundException("KSPKG file not found", kspkgPath);

        var paths = _gameDetection.DetectAll();
        if (paths.ModsFolder == null)
            throw new InvalidOperationException(
                "ACE mods folder could not be determined. Please launch Assetto Corsa EVO at least once.");

        var fi = new FileInfo(kspkgPath);
        var modName = Path.GetFileNameWithoutExtension(fi.Name);
        var destPath = Path.Combine(paths.ModsFolder, fi.Name);

        ct.ThrowIfCancellationRequested();

        File.Copy(kspkgPath, destPath, overwrite: true);
        Log.Information("Copied {Source} to {Dest}", kspkgPath, destPath);

        var manifest = new SidecarManifest
        {
            Name = modName,
            Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
            InstalledAt = DateTime.UtcNow.ToString("O")
        };

        var manifestPath = Path.Combine(paths.ModsFolder, $"{modName}.evomanifest.json");
        _modDiscovery.WriteSidecarManifest(manifestPath, manifest);
        Log.Information("Wrote sidecar manifest to {Path}", manifestPath);

        return Task.FromResult(modName);
    }
}
