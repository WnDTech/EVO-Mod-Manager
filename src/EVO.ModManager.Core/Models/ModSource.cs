namespace EVO.ModManager.Core.Models;

public class ModSource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RssFeedUrl { get; set; } = string.Empty;
    public string DownloadPageUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public static List<ModSource> Defaults => new()
    {
        new ModSource
        {
            Id = "overtake-gg",
            Name = "OverTake.gg",
            RssFeedUrl = "",
            DownloadPageUrl = "https://www.overtake.gg/downloads/categories/assetto-corsa-evo.275/"
        },
        new ModSource
        {
            Id = "overtake-forum",
            Name = "OverTake Forum",
            RssFeedUrl = "https://www.overtake.gg/forums/-/index.rss",
            DownloadPageUrl = "https://www.overtake.gg/forums/assetto-corsa-evo-mods.752/"
        }
    };
}
