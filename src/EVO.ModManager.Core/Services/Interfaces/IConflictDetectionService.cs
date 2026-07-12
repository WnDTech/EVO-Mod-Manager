using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IConflictDetectionService
{
    List<ModConflict> DetectConflicts(List<Mod> mods);
    void ResolveConflict(ModConflict conflict, ConflictResolution resolution, string? winnerModId);
    int ConflictCount(List<Mod> mods);
}
