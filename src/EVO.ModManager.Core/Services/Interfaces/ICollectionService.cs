using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface ICollectionService
{
    Task ExportCollectionAsync(IEnumerable<Mod> mods, string destinationPath);
    Task<CollectionResult> ImportCollectionAsync(string sourcePath, string modsFolder);
    Task ExportCollectionWithFilesAsync(IEnumerable<Mod> mods, string destinationZip, string modsFolder);
    Task ImportCollectionWithFilesAsync(string sourceZip, string destinationFolder, IProgress<double>? progress = null);
}
