namespace EVO.ModManager.Core.Models;

public class ModSource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "rss"; // "rss" or "json"
    public string RssFeedUrl { get; set; } = string.Empty;
    public string DownloadPageUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public static List<ModSource> Defaults => new()
    {
        new ModSource
        {
            Id = "overtake-global",
            Name = "OverTake.gg (Global)",
            SourceType = "rss",
            RssFeedUrl = "https://www.overtake.gg/forums/-/index.rss",
            DownloadPageUrl = "https://www.overtake.gg/forums/assetto-corsa-evo-mods.752/"
        },
        new ModSource
        {
            Id = "community-repo",
            Name = "Community Repo",
            SourceType = "json",
            DownloadPageUrl = "https://raw.githubusercontent.com/WnDTech/EVO-Mod-Manager/main/repository.json"
        }
    };
}
