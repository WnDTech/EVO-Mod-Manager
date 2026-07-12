namespace EVO.ModManager.Core.Services.Interfaces;

public interface ILiveryLabService
{
    bool IsInstalled { get; }
    Task AutoDownloadAsync(IProgress<double>? progress = null, CancellationToken ct = default);
    void LaunchWithZip(string zipPath);
    string? LiveryLabPath { get; }
}
