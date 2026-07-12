namespace EVO.ModManager.Core.Models;

public enum ConflictType
{
    NameCollision,
    HashMatch,
    SourceConflict
}

public enum ConflictResolution
{
    Unresolved,
    Winner,
    Ignored
}

public class ModConflict
{
    public int Id { get; set; }
    public string ModIdA { get; set; } = string.Empty;
    public string ModIdB { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public ConflictType ConflictType { get; set; }
    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
    public DateTime? ResolvedAt { get; set; }
}
