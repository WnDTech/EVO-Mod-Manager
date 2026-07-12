using EVO.ModManager.Core.Models;

namespace EVO.ModManager.Core.Services.Interfaces;

public interface IModBrowserService
{
    Task<List<ModSource>> GetSourcesAsync();
    Task<List<DownloadableMod>> FetchModListAsync(string sourceId, ModType? category = null,
        CancellationToken ct = default);
    Task<string> DownloadModAsync(DownloadableMod mod, string downloadDir,
        IProgress<double>? progress = null, CancellationToken ct = default);
}
