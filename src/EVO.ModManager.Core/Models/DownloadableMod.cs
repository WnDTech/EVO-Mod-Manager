namespace EVO.ModManager.Core.Models;

public class DownloadableMod
{
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public DateTime Published { get; set; }
    public string? Category { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ResourceId { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public ModType ModType { get; set; } = ModType.Unknown;
}
