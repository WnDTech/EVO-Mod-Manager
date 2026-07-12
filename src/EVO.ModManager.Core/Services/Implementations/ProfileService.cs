using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Data;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ProfileService : IProfileService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ProfileService>();
    private readonly DatabaseContext _db;

    public ProfileService(DatabaseContext db) => _db = db;

    public List<Profile> GetAll()
    {
        var profiles = new List<Profile>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Description, CreatedAt FROM Profiles ORDER BY CreatedAt";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var profile = new Profile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt = DateTime.Parse(reader.GetString(3))
            };
            profile.ModEntries = GetEntries(profile.Id);
            profiles.Add(profile);
        }
        return profiles;
    }

    public Profile? GetById(string id) => GetAll().FirstOrDefault(p => p.Id == id);
    public Profile? GetByName(string name) => GetAll().FirstOrDefault(p =>
        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Create(Profile profile)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Profiles (Id, Name, Description, CreatedAt) VALUES (@Id, @Name, @Desc, @Created)";
        cmd.Parameters.AddWithValue("@Id", profile.Id);
        cmd.Parameters.AddWithValue("@Name", profile.Name);
        cmd.Parameters.AddWithValue("@Desc", (object?)profile.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Created", profile.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        foreach (var entry in profile.ModEntries)
        {
            AddEntry(entry);
        }

        Log.Information("Profile created: {Name}", profile.Name);
    }

    public void Update(Profile profile)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "UPDATE Profiles SET Name = @Name, Description = @Desc WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", profile.Id);
        cmd.Parameters.AddWithValue("@Name", profile.Name);
        cmd.Parameters.AddWithValue("@Desc", (object?)profile.Description ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        using var del = _db.Connection.CreateCommand();
        del.CommandText = "DELETE FROM ProfileModEntries WHERE ProfileId = @Id";
        del.Parameters.AddWithValue("@Id", profile.Id);
        del.ExecuteNonQuery();

        foreach (var entry in profile.ModEntries)
            AddEntry(entry);
    }

    public void Delete(string id)
    {
        using var c1 = _db.Connection.CreateCommand();
        c1.CommandText = "DELETE FROM ProfileModEntries WHERE ProfileId = @Id";
        c1.Parameters.AddWithValue("@Id", id);
        c1.ExecuteNonQuery();

        using var c2 = _db.Connection.CreateCommand();
        c2.CommandText = "DELETE FROM Profiles WHERE Id = @Id";
        c2.Parameters.AddWithValue("@Id", id);
        c2.ExecuteNonQuery();
    }

    public Task ActivateAsync(string profileId, string modsFolder)
    {
        var profile = GetById(profileId);
        if (profile == null) return Task.CompletedTask;

        // First, disable all mods
        using var enableAll = _db.Connection.CreateCommand();
        enableAll.CommandText = "UPDATE Mods SET IsEnabled = 1";
        enableAll.ExecuteNonQuery();

        // Then apply profile states: disabled mods
        var disabledMods = profile.ModEntries.Where(e => !e.IsEnabled).ToList();
        foreach (var entry in disabledMods)
        {
            var mod = GetModById(entry.ModId);
            if (mod == null) continue;

            var disabledDir = Path.Combine(modsFolder, "_disabled");
            Directory.CreateDirectory(disabledDir);

            var sourcePath = Path.Combine(modsFolder, mod.FileName);
            if (File.Exists(sourcePath))
            {
                var destPath = Path.Combine(disabledDir, mod.FileName);
                File.Move(sourcePath, destPath);
            }

            using var upd = _db.Connection.CreateCommand();
            upd.CommandText = "UPDATE Mods SET IsEnabled = 0, DisabledPath = @dp WHERE Id = @Id";
            upd.Parameters.AddWithValue("@dp", Path.Combine(disabledDir, mod.FileName));
            upd.Parameters.AddWithValue("@Id", mod.Id);
            upd.ExecuteNonQuery();
        }

        // Enable mods that are in the profile as enabled
        var enabledMods = profile.ModEntries.Where(e => e.IsEnabled).ToList();
        foreach (var entry in enabledMods)
        {
            var mod = GetModById(entry.ModId);
            if (mod == null) continue;

            var disabledPath = mod.DisabledPath;
            if (disabledPath != null && File.Exists(disabledPath))
            {
                var destPath = Path.Combine(modsFolder, mod.FileName);
                File.Move(disabledPath, destPath);
            }

            using var upd = _db.Connection.CreateCommand();
            upd.CommandText = "UPDATE Mods SET IsEnabled = 1, DisabledPath = NULL WHERE Id = @Id";
            upd.Parameters.AddWithValue("@Id", mod.Id);
            upd.ExecuteNonQuery();
        }

        Log.Information("Profile activated: {Name} ({Id})", profile.Name, profileId);
        return Task.CompletedTask;
    }

    public string ExportToJson(string profileId)
    {
        var profile = GetById(profileId);
        if (profile == null) return "{}";

        var export = new
        {
            profile.Name,
            profile.Description,
            ExportedAt = DateTime.UtcNow.ToString("O"),
            ModEntries = profile.ModEntries.Select(e => new
            {
                ModId = e.ModId,
                IsEnabled = e.IsEnabled
            })
        };

        return System.Text.Json.JsonSerializer.Serialize(export, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public Profile ImportFromJson(string json)
    {
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var profile = new Profile
        {
            Name = root.GetProperty("Name").GetString() ?? "Imported Profile",
            Description = root.TryGetProperty("Description", out var desc) ? desc.GetString() : null
        };

        if (root.TryGetProperty("ModEntries", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                profile.ModEntries.Add(new ProfileModEntry
                {
                    ProfileId = profile.Id,
                    ModId = entry.GetProperty("ModId").GetString() ?? "",
                    IsEnabled = entry.GetProperty("IsEnabled").GetBoolean()
                });
            }
        }

        return profile;
    }

    private List<ProfileModEntry> GetEntries(string profileId)
    {
        var entries = new List<ProfileModEntry>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT Id, ProfileId, ModId, IsEnabled FROM ProfileModEntries WHERE ProfileId = @pid";
        cmd.Parameters.AddWithValue("@pid", profileId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new ProfileModEntry
            {
                Id = reader.GetInt32(0),
                ProfileId = reader.GetString(1),
                ModId = reader.GetString(2),
                IsEnabled = reader.GetInt32(3) == 1
            });
        }
        return entries;
    }

    private void AddEntry(ProfileModEntry entry)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO ProfileModEntries (ProfileId, ModId, IsEnabled) VALUES (@pid, @mid, @en)";
        cmd.Parameters.AddWithValue("@pid", entry.ProfileId);
        cmd.Parameters.AddWithValue("@mid", entry.ModId);
        cmd.Parameters.AddWithValue("@en", entry.IsEnabled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private Mod? GetModById(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Mods WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Mod
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            FileName = reader.GetString(3),
            IsEnabled = reader.GetInt32(13) == 1,
            DisabledPath = reader.IsDBNull(17) ? null : reader.GetString(17)
        };
    }
}
