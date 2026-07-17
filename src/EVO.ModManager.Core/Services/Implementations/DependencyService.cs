using Microsoft.Data.Sqlite;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Data;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class DependencyService : IDependencyService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<DependencyService>();
    private readonly DatabaseContext _db;

    public DependencyService(DatabaseContext db) => _db = db;

    public void AddDependency(string modId, string dependsOnModId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO ModDependencies (ModId, DependsOnModId)
            VALUES (@modId, @dependsOn)
            """;
        cmd.Parameters.AddWithValue("@modId", modId);
        cmd.Parameters.AddWithValue("@dependsOn", dependsOnModId);
        cmd.ExecuteNonQuery();
        Log.Information("Dependency added: {ModId} depends on {DependsOnModId}", modId, dependsOnModId);
    }

    public void RemoveDependency(string modId, string dependsOnModId)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM ModDependencies
            WHERE ModId = @modId AND DependsOnModId = @dependsOn
            """;
        cmd.Parameters.AddWithValue("@modId", modId);
        cmd.Parameters.AddWithValue("@dependsOn", dependsOnModId);
        cmd.ExecuteNonQuery();
        Log.Information("Dependency removed: {ModId} no longer depends on {DependsOnModId}", modId, dependsOnModId);
    }

    public List<Mod> GetDependencies(string modId)
    {
        var mods = new List<Mod>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.* FROM Mods m
            INNER JOIN ModDependencies d ON d.DependsOnModId = m.Id
            WHERE d.ModId = @modId
            """;
        cmd.Parameters.AddWithValue("@modId", modId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            mods.Add(MapMod(reader));
        return mods;
    }

    public List<Mod> GetDependents(string modId)
    {
        var mods = new List<Mod>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.* FROM Mods m
            INNER JOIN ModDependencies d ON d.ModId = m.Id
            WHERE d.DependsOnModId = @modId
            """;
        cmd.Parameters.AddWithValue("@modId", modId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            mods.Add(MapMod(reader));
        return mods;
    }

    public (bool canEnable, List<string> missingDeps) CanEnable(string modId, List<Mod> allMods)
    {
        var deps = GetDependencies(modId);
        var missing = new List<string>();

        foreach (var dep in deps)
        {
            var mod = allMods.FirstOrDefault(m => m.Id == dep.Id);
            if (mod == null || !mod.IsEnabled)
                missing.Add(dep.Name);
        }

        return (missing.Count == 0, missing);
    }

    public (bool canDisable, List<string> dependentMods) CanDisable(string modId, List<Mod> allMods)
    {
        var dependents = GetDependents(modId);
        var enabledDependents = new List<string>();

        foreach (var dep in dependents)
        {
            var mod = allMods.FirstOrDefault(m => m.Id == dep.Id);
            if (mod != null && mod.IsEnabled)
                enabledDependents.Add(dep.Name);
        }

        return (enabledDependents.Count == 0, enabledDependents);
    }

    private static Mod MapMod(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Name = r.GetString(1),
        ModType = Enum.TryParse<ModType>(r.GetString(2), out var t) ? t : ModType.Unknown,
        FileName = r.GetString(3),
        Version = r.IsDBNull(4) ? null : r.GetString(4),
        Author = r.IsDBNull(5) ? null : r.GetString(5),
        Description = r.IsDBNull(6) ? null : r.GetString(6),
        SizeBytes = r.GetInt64(7),
        InstalledAt = DateTime.Parse(r.GetString(8)),
        UpdatedAt = r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)),
        SourceUrl = r.IsDBNull(10) ? null : r.GetString(10),
        SourceModId = r.IsDBNull(11) ? null : r.GetString(11),
        StorageId = r.IsDBNull(12) ? null : r.GetString(12),
        IsEnabled = r.GetInt32(13) == 1,
        IsSymlinked = r.GetInt32(14) == 1,
        SymlinkTarget = r.IsDBNull(15) ? null : r.GetString(15),
        Hash = r.IsDBNull(16) ? null : r.GetString(16),
        DisabledPath = r.IsDBNull(17) ? null : r.GetString(17)
    };
}
