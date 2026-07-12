using System.Runtime.InteropServices;
using Serilog;
using Microsoft.Win32;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Data;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class StorageLocationService : IStorageLocationService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<StorageLocationService>();
    private readonly DatabaseContext _db;

    public StorageLocationService(DatabaseContext db) => _db = db;

    public List<StorageLocation> GetAll()
    {
        var locations = new List<StorageLocation>();
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Path, IsActive, IsDefault, CreatedAt FROM StorageLocations ORDER BY CreatedAt";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            locations.Add(new StorageLocation
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Path = reader.GetString(2),
                IsActive = reader.GetInt32(3) == 1,
                IsDefault = reader.GetInt32(4) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(5))
            });
        }
        return locations;
    }

    public StorageLocation? GetById(string id)
        => GetAll().FirstOrDefault(s => s.Id == id);

    public StorageLocation? GetDefault()
        => GetAll().FirstOrDefault(s => s.IsDefault && s.IsActive);

    public void Add(StorageLocation location)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO StorageLocations (Id, Name, Path, IsActive, IsDefault, CreatedAt)
            VALUES (@Id, @Name, @Path, @IsActive, @IsDefault, @CreatedAt)
            """;
        cmd.Parameters.AddWithValue("@Id", location.Id);
        cmd.Parameters.AddWithValue("@Name", location.Name);
        cmd.Parameters.AddWithValue("@Path", location.Path);
        cmd.Parameters.AddWithValue("@IsActive", location.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@IsDefault", location.IsDefault ? 1 : 0);
        cmd.Parameters.AddWithValue("@CreatedAt", location.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();

        Log.Information("Storage location added: {Name} -> {Path}", location.Name, location.Path);
    }

    public void Remove(string id)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM StorageLocations WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetDefault(string id)
    {
        // Clear existing default
        using var clear = _db.Connection.CreateCommand();
        clear.CommandText = "UPDATE StorageLocations SET IsDefault = 0";
        clear.ExecuteNonQuery();

        // Set new default
        using var set = _db.Connection.CreateCommand();
        set.CommandText = "UPDATE StorageLocations SET IsDefault = 1 WHERE Id = @id";
        set.Parameters.AddWithValue("@id", id);
        set.ExecuteNonQuery();
    }

    public bool SymlinkMod(string modsFolder, string modName, string storagePath)
    {
        var linkPath = Path.Combine(modsFolder, modName);

        if (SymlinkExists(modsFolder, modName))
        {
            Log.Warning("Symlink already exists: {Path}", linkPath);
            return false;
        }

        if (Directory.Exists(linkPath) || File.Exists(linkPath))
        {
            Log.Warning("Path already exists and is not a symlink: {Path}", linkPath);
            return false;
        }

        Directory.CreateDirectory(storagePath);

        if (NativeMethods.CreateSymbolicLink(linkPath, storagePath, SymbolicLinkFlags.Directory))
        {
            Log.Information("Symlink created: {Link} -> {Target}", linkPath, storagePath);
            return true;
        }

        var error = Marshal.GetLastWin32Error();

        if (error == 1314) // ERROR_PRIVILEGE_NOT_HELD
        {
            if (IsDeveloperModeEnabled())
            {
                Log.Error("Symlink creation failed with error {Error} even with Developer Mode enabled", error);
                return false;
            }

            Log.Warning("Symlink creation requires admin rights. Developer Mode is off.");
            // The caller should handle UAC prompt
            return false;
        }

        Log.Error("Symlink creation failed (error {Error}): {Link} -> {Target}", error, linkPath, storagePath);
        return false;
    }

    public bool RemoveSymlink(string modsFolder, string modName)
    {
        var linkPath = Path.Combine(modsFolder, modName);
        if (!SymlinkExists(modsFolder, modName)) return false;

        try
        {
            Directory.Delete(linkPath);
            Log.Information("Symlink removed: {Path}", linkPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove symlink {Path}", linkPath);
            return false;
        }
    }

    public bool SymlinkExists(string modsFolder, string modName)
    {
        var path = Path.Combine(modsFolder, modName);
        try
        {
            var di = new DirectoryInfo(path);
            return di.Exists && (di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch { return false; }
    }

    public bool IsSymlinkBroken(string modsFolder, string modName)
    {
        var path = Path.Combine(modsFolder, modName);
        try
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists && (di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                return true; // reparse point exists but target is gone
            if (di.Exists && (di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                // Try to list entries to verify target is accessible
                try { di.GetFileSystemInfos(); return false; }
                catch { return true; }
            }
            return false;
        }
        catch { return false; }
    }

    public long GetFreeSpace(string path)
    {
        try
        {
            var driveName = Path.GetPathRoot(path);
            if (driveName != null)
            {
                var drive = new DriveInfo(driveName);
                if (drive.IsReady)
                    return drive.AvailableFreeSpace;
            }
        }
        catch { }
        return 0;
    }

    public static bool IsDeveloperModeEnabled()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock",
                "AllowDevelopmentWithoutDevLicense", 0);
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool CreateSymbolicLink(
            string lpSymlinkFileName,
            string lpTargetFileName,
            SymbolicLinkFlags dwFlags);
    }

    private enum SymbolicLinkFlags : uint
    {
        File = 0,
        Directory = 1
    }
}
