using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IBackupService
{
    Task BackupFullAsync(string modsFolder, string destinationZip,
        IProgress<double>? progress = null, CancellationToken ct = default);
    Task BackupSelectedAsync(string modsFolder, string destinationZip, List<Mod> mods,
        IProgress<double>? progress = null, CancellationToken ct = default);
    Task<List<Mod>> PreviewBackupAsync(string backupZip);
    Task RestoreAsync(string backupZip, string modsFolder, List<string>? modIdsToRestore = null,
        IProgress<double>? progress = null, CancellationToken ct = default);
}
