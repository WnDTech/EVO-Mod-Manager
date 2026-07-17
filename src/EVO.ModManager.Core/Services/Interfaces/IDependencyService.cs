using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IDependencyService
{
    void AddDependency(string modId, string dependsOnModId);
    void RemoveDependency(string modId, string dependsOnModId);
    List<Mod> GetDependencies(string modId);
    List<Mod> GetDependents(string modId);
    (bool canEnable, List<string> missingDeps) CanEnable(string modId, List<Mod> allMods);
    (bool canDisable, List<string> dependentMods) CanDisable(string modId, List<Mod> allMods);
}
