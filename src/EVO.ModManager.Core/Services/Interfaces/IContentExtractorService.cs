namespace EVO.ModManager.Core.Services.Interfaces;

public class ContentExtractResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ExtractedPath { get; set; }
}

public interface IContentExtractorService
{
    bool SdkIsInstalled { get; }
    bool AceModderContentExists { get; }
    string? AceModderContentPath { get; }

    Task<ContentExtractResult> ExtractAsync(
        string contentKspkgPath,
        string outputDirectory,
        CancellationToken ct = default);
}
