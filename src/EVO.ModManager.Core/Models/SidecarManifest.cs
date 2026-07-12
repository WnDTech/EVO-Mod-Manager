using System.Text.Json.Serialization;

namespace EVO.ModManager.Core.Models;

public class SidecarManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Unknown";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("sourceModId")]
    public string? SourceModId { get; set; }

    [JsonPropertyName("installedAt")]
    public string InstalledAt { get; set; } = DateTime.UtcNow.ToString("O");

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}
