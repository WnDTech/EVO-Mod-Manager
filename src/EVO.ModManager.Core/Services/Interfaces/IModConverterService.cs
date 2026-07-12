namespace EVO.ModManager.Core.Services.Interfaces;

public class ConversionResult
{
    public bool Success { get; set; }
    public string? OutputKspkgPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ModName { get; set; }
}

public interface IModConverterService
{
    bool IsSdkAvailable { get; }
    Task<ConversionResult> ConvertAcModAsync(string sourcePath, string outputDir,
        IProgress<double>? progress = null, CancellationToken ct = default);
}
