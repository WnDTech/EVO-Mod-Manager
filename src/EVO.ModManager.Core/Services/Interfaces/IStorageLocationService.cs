using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IStorageLocationService
{
    List<StorageLocation> GetAll();
    StorageLocation? GetById(string id);
    StorageLocation? GetDefault();
    void Add(StorageLocation location);
    void Remove(string id);
    void SetDefault(string id);
    bool SymlinkMod(string modsFolder, string modName, string storagePath);
    bool RemoveSymlink(string modsFolder, string modName);
    bool SymlinkExists(string modsFolder, string modName);
    bool IsSymlinkBroken(string modsFolder, string modName);
    long GetFreeSpace(string path);
}
