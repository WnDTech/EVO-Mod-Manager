using System.Text.Json.Serialization;

namespace EVO.ModManager.Core.Models;

public class CollectionFile
{
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "1.0.0";

    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("mods")]
    public List<CollectionModEntry> Mods { get; set; } = new();
}
