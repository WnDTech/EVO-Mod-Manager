using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class ContentExtractorService : IContentExtractorService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ContentExtractorService>();

    private const string AceModderFolder = "ACE-Modder";
    private const string AceContentSubDir = "content";
    private const string TargetContentDir = "content";

    public bool SdkIsInstalled { get; private set; }
    public bool AceModderContentExists { get; private set; }
    public string? AceModderContentPath { get; private set; }

    public ContentExtractorService()
    {
        DetectSdk();
        DetectAceModderContent();
    }

    public async Task<ContentExtractResult> ExtractAsync(
        string contentKspkgPath,
        string outputDirectory,
        CancellationToken ct = default)
    {
        var result = new ContentExtractResult();

        if (string.IsNullOrWhiteSpace(contentKspkgPath))
        {
            result.Message = "No content.kspkg path provided.";
            return result;
        }

        if (!File.Exists(contentKspkgPath))
        {
            result.Message = $"content.kspkg not found at: {contentKspkgPath}";
            return result;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);

            if (AceModderContentExists && AceModderContentPath != null)
            {
                return await CopyFromAceModderAsync(AceModderContentPath, outputDirectory, ct);
            }

            if (SdkIsInstalled)
            {
                result.Success = true;
                result.Message = string.Join("\n",
                    "ACE Editor SDK is installed but cannot extract content.kspkg directly.",
                    "",
                    "To extract content.kspkg manually:",
                    "  1. Launch the ACE Editor (AssettoCorsaEVOEditor.exe)",
                    "  2. Open the Content Browser (Window > Content Browser)",
                    "  3. Right-click the content root and select 'Export All Content'",
                    "  4. Choose the output directory: " + outputDirectory,
                    "",
                    "Alternatively, use the ACE Modder tool to extract content automatically.",
                    "  - Download from: https://github.com/compuvised/ACE-Modder",
                    "  - Place it in: %USERPROFILE%\\Saved Games\\ACE-Modder\\",
                    "  - Run ACE-Modder and select 'Extract content.kspkg'",
                    "",
                    "After extraction, re-run this operation to copy the extracted files.");
                return result;
            }

            result.Success = true;
            result.Message = string.Join("\n",
                "content.kspkg is a proprietary encrypted format and cannot be extracted directly.",
                "",
                "To extract content.kspkg, you have the following options:",
                "",
                "Option 1 — ACE Editor (recommended):",
                "  Install the ACE Editor SDK via Steam (App 4813350), then:",
                "  - Launch the editor and use Window > Content Browser > Export All Content",
                "  - Export to: " + outputDirectory,
                "",
                "Option 2 — ACE-Modder:",
                "  Download from: https://github.com/compuvised/ACE-Modder",
                "  - Extract to: %USERPROFILE%\\Saved Games\\ACE-Modder\\",
                "  - Run ACE-Modder.exe and choose 'Extract content.kspkg'",
                "",
                "Option 3 — LiveryLab:",
                "  LiveryLab can extract content.kspkg for livery creation.",
                "  - Install LiveryLab via the Mod Manager's Tools menu",
                "  - Open LiveryLab and use File > Import Game Content",
                "",
                "After extraction, run content extraction again to copy the files automatically.");
            return result;
        }
        catch (OperationCanceledException)
        {
            Log.Information("Content extraction cancelled by user");
            result.Message = "Content extraction was cancelled.";
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to extract content from {Path}", contentKspkgPath);
            result.Message = $"Extraction failed: {ex.Message}";
            return result;
        }
    }

    private async Task<ContentExtractResult> CopyFromAceModderAsync(
        string sourceContentPath,
        string outputDirectory,
        CancellationToken ct)
    {
        var result = new ContentExtractResult();

        try
        {
            var targetDir = Path.Combine(outputDirectory, TargetContentDir);
            Directory.CreateDirectory(targetDir);

            var files = Directory.GetFiles(sourceContentPath, "*", SearchOption.AllDirectories);
            var total = files.Length;
            var copied = 0;

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceContentPath, filePath);
                var destPath = Path.Combine(targetDir, relativePath);
                var destSubDir = Path.GetDirectoryName(destPath);

                if (destSubDir != null)
                {
                    Directory.CreateDirectory(destSubDir);
                }

                await CopyFileAsync(filePath, destPath);
                copied++;
            }

            result.Success = true;
            result.ExtractedPath = targetDir;
            result.Message = $"Successfully copied {copied} file(s) from ACE-Modder content to {targetDir}";

            Log.Information("Copied {Count} content files from {Src} to {Dst}",
                copied, sourceContentPath, targetDir);
        }
        catch (OperationCanceledException)
        {
            result.Message = "Content copy was cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy content from ACE-Modder");
            result.Message = $"Failed to copy content: {ex.Message}";
        }

        return result;
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath)
    {
        await using var sourceStream = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);

        await using var destStream = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true);

        await sourceStream.CopyToAsync(destStream);
    }

    private void DetectSdk()
    {
        var possiblePaths = new[]
        {
            @"G:\GAMES\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK",
            @"D:\SteamLibrary\steamapps\common\Assetto Corsa EVO SDK"
        };

        foreach (var dir in possiblePaths)
        {
            if (File.Exists(Path.Combine(dir, "AssettoCorsaEVOEditor.exe")))
            {
                SdkIsInstalled = true;
                Log.Information("ACE Editor SDK detected at {Path}", dir);
                return;
            }
        }

        Log.Information("ACE Editor SDK not found");
    }

    private void DetectAceModderContent()
    {
        var savedGames = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games",
            AceModderFolder,
            AceContentSubDir);

        if (Directory.Exists(savedGames))
        {
            AceModderContentPath = savedGames;
            AceModderContentExists = true;
            Log.Information("ACE-Modder extracted content found at {Path}", savedGames);
        }
        else
        {
            Log.Information("ACE-Modder extracted content not found at {Path}", savedGames);
        }
    }
}
