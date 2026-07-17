using System.Diagnostics;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class EditorService : IEditorService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<EditorService>();

    public string? EditorPath { get; private set; }
    public string? GamePath { get; private set; }

    public bool IsEditorAvailable
    {
        get
        {
            EditorPath ??= DetectEditor();
            GamePath ??= DetectGamePath();
            return EditorPath != null;
        }
    }

    public void LaunchEditor(string? modPath = null)
    {
        if (!IsEditorAvailable || EditorPath == null)
        {
            Log.Warning("Editor not available, cannot launch");
            return;
        }

        try
        {
            // Editor needs game directory as working directory to find content.kspkg
            var workDir = GamePath ?? Path.GetDirectoryName(EditorPath);

            var psi = new ProcessStartInfo(EditorPath)
            {
                WorkingDirectory = workDir,
                UseShellExecute = true
            };

            if (modPath != null)
                psi.Arguments = $"\"{modPath}\"";

            Process.Start(psi);
            Log.Information("Launched editor: {Path} (wd: {WD}) {Args}", EditorPath, workDir, psi.Arguments);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch editor at {Path}", EditorPath);
        }
    }

    private static string? DetectEditor()
    {
        var possiblePaths = new[]
        {
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK",
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK"
        };

        foreach (var dir in possiblePaths)
        {
            var exePath = Path.Combine(dir, "AssettoCorsaEVOEditor.exe");
            if (File.Exists(exePath))
                return exePath;
        }
        return null;
    }

    private static string? DetectGamePath()
    {
        var candidates = new[]
        {
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO",
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO",
            @"C:\Program Files (x86)\Steam\steamapps\common\Assetto Corsa EVO"
        };

        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "AssettoCorsaEVO.exe")))
                return dir;
        }
        return null;
    }
}
