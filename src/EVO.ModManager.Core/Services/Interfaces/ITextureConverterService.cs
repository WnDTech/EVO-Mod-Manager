namespace EVO.ModManager.Core.Services.Interfaces;

public class TextureConvertResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TextureFilePath { get; set; }
    public string? TextureMipsFilePath { get; set; }
}

public interface ITextureConverterService
{
    Task<TextureConvertResult> ConvertAsync(
        string sourceImagePath,
        string outputDirectory,
        CancellationToken ct = default);
}
