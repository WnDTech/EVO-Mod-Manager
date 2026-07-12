using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ConflictDetectionService : IConflictDetectionService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ConflictDetectionService>();
    private readonly List<ModConflict> _conflicts = new();

    public List<ModConflict> DetectConflicts(List<Mod> mods)
    {
        _conflicts.Clear();

        // Level 1: Name collision — same filename
        var nameGroups = mods.GroupBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in nameGroups)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    _conflicts.Add(new ModConflict
                    {
                        ModIdA = list[i].Id,
                        ModIdB = list[j].Id,
                        FilePath = group.Key,
                        ConflictType = ConflictType.NameCollision
                    });
                }
            }
        }

        // Level 2: Hash match — identical content
        var hashGroups = mods.Where(m => !string.IsNullOrEmpty(m.Hash))
            .GroupBy(m => m.Hash!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in hashGroups)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    // Skip if already detected as name collision
                    if (_conflicts.Any(c =>
                        (c.ModIdA == list[i].Id && c.ModIdB == list[j].Id) ||
                        (c.ModIdA == list[j].Id && c.ModIdB == list[i].Id)))
                        continue;

                    _conflicts.Add(new ModConflict
                    {
                        ModIdA = list[i].Id,
                        ModIdB = list[j].Id,
                        FilePath = list[i].FileName,
                        ConflictType = ConflictType.HashMatch
                    });
                }
            }
        }

        // Level 3: Source conflict — same OverTake.gg resource ID
        var sourceGroups = mods.Where(m => !string.IsNullOrEmpty(m.SourceModId))
            .GroupBy(m => m.SourceModId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in sourceGroups)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    // Skip if already detected
                    if (_conflicts.Any(c =>
                        (c.ModIdA == list[i].Id && c.ModIdB == list[j].Id) ||
                        (c.ModIdA == list[j].Id && c.ModIdB == list[i].Id)))
                        continue;

                    _conflicts.Add(new ModConflict
                    {
                        ModIdA = list[i].Id,
                        ModIdB = list[j].Id,
                        FilePath = $"{list[i].Name} (source: {group.Key})",
                        ConflictType = ConflictType.SourceConflict
                    });
                }
            }
        }

        Log.Information("Conflict detection complete: {Count} conflicts found", _conflicts.Count);
        return new List<ModConflict>(_conflicts);
    }

    public void ResolveConflict(ModConflict conflict, ConflictResolution resolution, string? winnerModId)
    {
        conflict.Resolution = resolution;
        conflict.ResolvedAt = DateTime.UtcNow;

        Log.Information("Conflict resolved: {Type} between {A} and {B} -> {Resolution} (winner: {Winner})",
            conflict.ConflictType, conflict.ModIdA, conflict.ModIdB, resolution, winnerModId);
    }

    public int ConflictCount(List<Mod> mods)
    {
        if (_conflicts.Count == 0) return 0;
        return _conflicts.Count(c => c.Resolution == ConflictResolution.Unresolved);
    }
}
