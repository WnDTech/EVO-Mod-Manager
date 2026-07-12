using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace EVO.ModManager.App.Services;

public class UpdateInfo
{
    public string? LatestVersion { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public bool HasUpdate { get; set; }
}

public class UpdateCheckService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<UpdateCheckService>();
    private const string GitHubApiUrl = "https://api.github.com/repos/WnDTech/EVO-Mod-Manager/releases/latest";
    private static readonly HttpClient Client = new();

    public UpdateCheckService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("EVO-Mod-Manager/1.0");
    }

    public async Task<UpdateInfo> CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            var response = await Client.GetAsync(GitHubApiUrl);

            // If no releases exist yet, just return silently
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new UpdateInfo { HasUpdate = false };

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var latestTag = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = latestTag.TrimStart('v');
            var downloadUrl = root.GetProperty("html_url").GetString() ?? "";
            var body = root.TryGetProperty("body", out var b) ? b.GetString() : null;

            var current = Version.TryParse(currentVersion, out var cv) ? cv : new Version(0, 0);
            var latest = Version.TryParse(latestVersion, out var lv) ? lv : new Version(0, 0);

            var info = new UpdateInfo
            {
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = body?.Length > 500 ? body[..500] + "..." : body,
                HasUpdate = latest > current
            };

            if (info.HasUpdate)
                Log.Information("Update available: {Current} -> {Latest}", currentVersion, latestVersion);

            return info;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
            return new UpdateInfo { HasUpdate = false };
        }
    }
}
