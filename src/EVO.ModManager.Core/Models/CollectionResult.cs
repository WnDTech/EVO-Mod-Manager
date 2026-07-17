namespace EVO.ModManager.Core.Models;

public class CollectionResult
{
    public List<CollectionModEntry> ImportedMods { get; set; } = new();
    public List<CollectionModEntry> AlreadyInstalled { get; set; } = new();
    public List<CollectionModEntry> MissingFromDisk { get; set; } = new();
}
