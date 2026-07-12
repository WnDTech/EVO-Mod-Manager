using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IModDiscoveryService
{
    Task<List<Mod>> ScanModsFolderAsync(string modsFolder, CancellationToken ct = default);
    Task<Mod?> GetModFromKspkgAsync(string kspkgPath, CancellationToken ct = default);
    SidecarManifest? ReadSidecarManifest(string manifestPath);
    void WriteSidecarManifest(string manifestPath, SidecarManifest manifest);
    ModType ClassifyMod(string modName, string? sourceUrl);
}
