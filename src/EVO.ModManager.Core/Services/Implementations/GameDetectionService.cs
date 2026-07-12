using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class GameDetectionService : IGameDetectionService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<GameDetectionService>();
    private const int GameAppId = 3058630;
    private const int SdkAppId = 4813350;

    public GamePaths DetectAll()
    {
        var paths = new GamePaths();
        paths.GamePath = FindGamePath();
        paths.SdkPath = FindSdkPath();
        paths.AceUserFolder = FindAceUserFolder();

        if (paths.AceUserFolder != null)
        {
            paths.ModsFolder = EnsureModsFolder(paths.AceUserFolder);
            paths.AceFolderCreated = true;
        }
        else
        {
            Log.Warning("ACE user folder not found; mods folder not created");
        }

        Log.Information("Game detection complete: Game={Game}, SDK={Sdk}, ACE={Ace}, Mods={Mods}",
            paths.GamePath, paths.SdkPath, paths.AceUserFolder, paths.ModsFolder);
        return paths;
    }

    public string? FindGamePath()
    {
        var candidates = new List<string>();

        try
        {
            var steamPath = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (steamPath != null)
            {
                var defaultLib = Path.Combine(steamPath, "steamapps", "common", "Assetto Corsa EVO");
                candidates.Add(defaultLib);

                var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    var vdf = File.ReadAllText(vdfPath);
                    var matches = System.Text.RegularExpressions.Regex.Matches(vdf, "\"(\\d+)\"\\s*\\n?\\s*\"([^\"]+)\"");
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        var libPath = m.Groups[2].Value.Replace("\\\\", "\\");
                        var gameDir = Path.Combine(libPath, "steamapps", "common", "Assetto Corsa EVO");
                        candidates.Add(gameDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read Steam registry for game detection");
        }

        foreach (var dir in candidates.Distinct())
        {
            var exePath = Path.Combine(dir, "AssettoCorsaEVO.exe");
            if (File.Exists(exePath))
            {
                Log.Information("Game found at {Path}", dir);
                return dir;
            }
        }

        Log.Warning("Game not found via Steam. Check app manifest directories...");

        // Fallback: scan common Steam library roots
        var fallbackRoots = new[] { "D:\\SteamLibrary", "E:\\SteamLibrary", "F:\\SteamLibrary", "G:\\SteamLibrary" };
        foreach (var root in fallbackRoots)
        {
            var dir = Path.Combine(root, "steamapps", "common", "Assetto Corsa EVO");
            if (File.Exists(Path.Combine(dir, "AssettoCorsaEVO.exe")))
            {
                Log.Information("Game found via fallback at {Path}", dir);
                return dir;
            }
        }

        Log.Warning("Game not found in any location");
        return null;
    }

    public string? FindSdkPath()
    {
        if (FindGamePath() is { } gamePath)
        {
            var sdkDir = Path.Combine(
                Path.GetDirectoryName(Path.GetDirectoryName(gamePath)) ?? "",
                "Assetto Corsa EVO SDK");
            if (File.Exists(Path.Combine(sdkDir, "AssettoCorsaEVOEditor.exe")))
                return sdkDir;
        }

        var altSdkDir = @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK";
        if (File.Exists(Path.Combine(altSdkDir, "AssettoCorsaEVOEditor.exe")))
            return altSdkDir;

        return null;
    }

    public string? FindAceUserFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var savedGames = Path.Combine(userProfile, "Saved Games", "ACE");
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ACE");

        if (Directory.Exists(savedGames))
        {
            Log.Information("ACE user folder found at {Path}", savedGames);
            return savedGames;
        }

        if (Directory.Exists(documents))
        {
            Log.Information("ACE user folder found at {Path} (Documents fallback)", documents);
            return documents;
        }

        Log.Warning("ACE user folder not found, will create at default path");
        return savedGames;
    }

    public string EnsureModsFolder(string aceUserFolder)
    {
        var modsPath = Path.Combine(aceUserFolder, "mods");
        if (!Directory.Exists(modsPath))
        {
            Directory.CreateDirectory(modsPath);
            Log.Information("Created mods folder at {Path}", modsPath);
        }
        return modsPath;
    }
}
