namespace EVO.ModManager.Core.Services.Interfaces;

public class ArchiveAnalysisResult
{
    public bool HasKspkg { get; set; }
    public bool HasCardesign { get; set; }
    public bool IsAcMod { get; set; }
    public List<string> KspkgFiles { get; set; } = new();
    public List<string> CardesignFiles { get; set; } = new();
    public string? SuggestedModName { get; set; }
    public string? ArchiveFormat { get; set; }
}

public interface IArchiveService
{
    ArchiveAnalysisResult AnalyzeArchive(string archivePath);
    Task ExtractArchiveAsync(string archivePath, string destinationDir,
        IProgress<double>? progress = null, CancellationToken ct = default);
    bool IsSupportedArchive(string filePath);
}
