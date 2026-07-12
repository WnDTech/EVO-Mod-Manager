namespace EVO.ModManager.Core.Models;

public enum ModType
{
    Unknown,
    Car,
    Track,
    Skin,
    Sound,
    App,
    Misc
}

public class Mod
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public ModType ModType { get; set; } = ModType.Unknown;
    public string FileName { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public long SizeBytes { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceModId { get; set; }
    public string? StorageId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSymlinked { get; set; }
    public string? SymlinkTarget { get; set; }
    public string? Hash { get; set; }
    public string? DisabledPath { get; set; }
}
