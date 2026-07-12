using Microsoft.Data.Sqlite;
using Serilog;

namespace EVO.ModManager.Core.Data;

public class DatabaseContext : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly ILogger Log = Serilog.Log.ForContext<DatabaseContext>();

    public DatabaseContext(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Mods (
                Id              TEXT PRIMARY KEY,
                Name            TEXT NOT NULL,
                ModType         TEXT NOT NULL,
                FileName        TEXT NOT NULL,
                Version         TEXT,
                Author          TEXT,
                Description     TEXT,
                SizeBytes       INTEGER DEFAULT 0,
                InstalledAt     TEXT NOT NULL,
                UpdatedAt       TEXT,
                SourceUrl       TEXT,
                SourceModId     TEXT,
                StorageId       TEXT,
                IsEnabled       INTEGER DEFAULT 1,
                IsSymlinked     INTEGER DEFAULT 0,
                SymlinkTarget   TEXT,
                Hash            TEXT,
                DisabledPath    TEXT
            );

            CREATE TABLE IF NOT EXISTS StorageLocations (
                Id              TEXT PRIMARY KEY,
                Name            TEXT NOT NULL,
                Path            TEXT NOT NULL UNIQUE,
                IsActive        INTEGER DEFAULT 1,
                IsDefault       INTEGER DEFAULT 0,
                CreatedAt       TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Profiles (
                Id              TEXT PRIMARY KEY,
                Name            TEXT NOT NULL UNIQUE,
                Description     TEXT,
                CreatedAt       TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProfileModEntries (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId       TEXT NOT NULL REFERENCES Profiles(Id) ON DELETE CASCADE,
                ModId           TEXT NOT NULL REFERENCES Mods(Id) ON DELETE CASCADE,
                IsEnabled       INTEGER DEFAULT 1,
                UNIQUE(ProfileId, ModId)
            );

            CREATE TABLE IF NOT EXISTS ModConflicts (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                ModIdA          TEXT NOT NULL,
                ModIdB          TEXT NOT NULL,
                FilePath        TEXT NOT NULL,
                ConflictType    TEXT NOT NULL,
                Resolution      TEXT,
                ResolvedAt      TEXT
            );

            CREATE TABLE IF NOT EXISTS KnownDownloads (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                SourceModId     TEXT NOT NULL,
                SourceUrl       TEXT NOT NULL,
                ModName         TEXT NOT NULL,
                InstalledModId  TEXT,
                LastChecked     TEXT,
                LatestVersion   TEXT
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key             TEXT PRIMARY KEY,
                Value           TEXT
            );
            """;

        cmd.ExecuteNonQuery();
        Log.Information("Database schema initialized");
    }

    public SqliteConnection Connection => _connection;

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
