namespace EVO.ModManager.Core.Models;

public class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ProfileModEntry> ModEntries { get; set; } = new();
}

public class ProfileModEntry
{
    public int Id { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string ModId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
