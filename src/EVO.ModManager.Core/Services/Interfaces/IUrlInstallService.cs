namespace EVO.ModManager.Core.Services.Interfaces;

public class UrlInstallResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ModName { get; set; }
}

public interface IUrlInstallService
{
    Task<UrlInstallResult> InstallFromUrlAsync(string url,
        IProgress<double>? progress = null, CancellationToken ct = default);
}
