namespace EVO.ModManager.Core.Services.Interfaces;

public class GamePaths
{
    public string? GamePath { get; set; }
    public string? SdkPath { get; set; }
    public string? AceUserFolder { get; set; }
    public string? ModsFolder { get; set; }
    public bool AceFolderCreated { get; set; }
}

public interface IGameDetectionService
{
    GamePaths DetectAll();
    string? FindGamePath();
    string? FindSdkPath();
    string? FindAceUserFolder();
    string EnsureModsFolder(string aceUserFolder);
}
