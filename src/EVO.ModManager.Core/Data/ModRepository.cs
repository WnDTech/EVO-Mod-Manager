using Microsoft.Data.Sqlite;
using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Data;

public class ModRepository
{
    private readonly DatabaseContext _db;

    public ModRepository(DatabaseContext db) => _db = db;

    public List<Mod> GetAll()
    {
        var mods = new List<Mod>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Mods";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            mods.Add(MapMod(reader));
        return mods;
    }

    public Mod? GetById(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Mods WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapMod(reader) : null;
    }

    public void Upsert(Mod mod)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Mods (Id, Name, ModType, FileName, Version, Author, Description,
                SizeBytes, InstalledAt, UpdatedAt, SourceUrl, SourceModId, StorageId,
                IsEnabled, IsSymlinked, SymlinkTarget, Hash, DisabledPath)
            VALUES (@Id, @Name, @ModType, @FileName, @Version, @Author, @Description,
                @SizeBytes, @InstalledAt, @UpdatedAt, @SourceUrl, @SourceModId, @StorageId,
                @IsEnabled, @IsSymlinked, @SymlinkTarget, @Hash, @DisabledPath)
            ON CONFLICT(Id) DO UPDATE SET
                Name=excluded.Name, ModType=excluded.ModType, FileName=excluded.FileName,
                Version=excluded.Version, Author=excluded.Author, Description=excluded.Description,
                SizeBytes=excluded.SizeBytes, UpdatedAt=excluded.UpdatedAt,
                SourceUrl=excluded.SourceUrl, SourceModId=excluded.SourceModId,
                StorageId=excluded.StorageId, IsEnabled=excluded.IsEnabled,
                IsSymlinked=excluded.IsSymlinked, SymlinkTarget=excluded.SymlinkTarget,
                Hash=excluded.Hash, DisabledPath=excluded.DisabledPath
            """;
        AddModParameters(cmd, mod);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Mods WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteAll()
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Mods";
        cmd.ExecuteNonQuery();
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

    private static void AddModParameters(SqliteCommand cmd, Mod mod)
    {
        cmd.Parameters.AddWithValue("@Id", mod.Id);
        cmd.Parameters.AddWithValue("@Name", mod.Name);
        cmd.Parameters.AddWithValue("@ModType", mod.ModType.ToString());
        cmd.Parameters.AddWithValue("@FileName", mod.FileName);
        cmd.Parameters.AddWithValue("@Version", (object?)mod.Version ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Author", (object?)mod.Author ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Description", (object?)mod.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SizeBytes", mod.SizeBytes);
        cmd.Parameters.AddWithValue("@InstalledAt", mod.InstalledAt.ToString("O"));
        cmd.Parameters.AddWithValue("@UpdatedAt", mod.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@SourceUrl", (object?)mod.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SourceModId", (object?)mod.SourceModId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StorageId", (object?)mod.StorageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsEnabled", mod.IsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsSymlinked", mod.IsSymlinked ? 1 : 0);
        cmd.Parameters.AddWithValue("@SymlinkTarget", (object?)mod.SymlinkTarget ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Hash", (object?)mod.Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@DisabledPath", (object?)mod.DisabledPath ?? DBNull.Value);
    }
}
