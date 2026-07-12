using Microsoft.Data.Sqlite;
using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Data;

public class SettingsRepository
{
    private readonly DatabaseContext _db;

    public SettingsRepository(DatabaseContext db) => _db = db;

    public string? Get(string key)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Settings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, string> GetAll()
    {
        var result = new Dictionary<string, string>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
}
