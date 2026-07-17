using System.Text.Json.Serialization;

namespace EVO.ModManager.Core.Models;

public class CollectionModEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("modType")]
    public string ModType { get; set; } = "Unknown";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }
}
