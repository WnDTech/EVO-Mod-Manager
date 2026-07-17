using System.Text.Json;
using Serilog;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class SkinInstallerService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SkinInstallerService>();

    public bool CanInstall(string archivePath) => HasCardesignFiles(archivePath);

    public async Task<SkinInstallResult> InstallSkinAsync(string archivePath, string aceContentFolder,
        CancellationToken ct = default)
    {
        var result = new SkinInstallResult();
        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "skins", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Step 1: Extract archive
            Log.Information("Extracting skin archive: {Path}", archivePath);
            var archiveService = new ArchiveService();
            await archiveService.ExtractArchiveAsync(archivePath, tempDir, null, ct);

            // Step 2: Find all skin files
            var cardesignFiles = Directory.GetFiles(tempDir, "*.cardesign", SearchOption.AllDirectories);
            var textureFiles = Directory.GetFiles(tempDir, "*.texture", SearchOption.AllDirectories);
            var textureMipsFiles = Directory.GetFiles(tempDir, "*.texturemips", SearchOption.AllDirectories);

            result.CardesignCount = cardesignFiles.Length;
            result.TextureCount = textureFiles.Length;

            if (cardesignFiles.Length == 0)
            {
                // No cardesign found — use archive name for the skin
                var archiveName = Path.GetFileNameWithoutExtension(archivePath);
                var targetDir = Path.Combine(aceContentFolder, "skins", archiveName);
                Directory.CreateDirectory(targetDir);
                CopySkinFiles(tempDir, targetDir, textureFiles, textureMipsFiles);
                result.SkinName = archiveName;
                result.TargetPath = targetDir;
                Log.Information("No cardesign found. Installed as: {Name}", archiveName);
            }
            else
            {
                // Parse each cardesign file
                foreach (var cdFile in cardesignFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var skinInfo = ParseCardesign(cdFile);
                    var targetDir = Path.Combine(aceContentFolder, "cars", skinInfo.CarId, "skins", skinInfo.SkinName);
                    Directory.CreateDirectory(targetDir);

                    // Copy cardesign
                    File.Copy(cdFile, Path.Combine(targetDir, Path.GetFileName(cdFile)), true);

                    // Copy associated textures
                    var cdDir = Path.GetDirectoryName(cdFile)!;
                    foreach (var tex in Directory.GetFiles(cdDir, "*.texture").Concat(Directory.GetFiles(cdDir, "*.texturemips")))
                    {
                        File.Copy(tex, Path.Combine(targetDir, Path.GetFileName(tex)), true);
                    }

                    result.InstalledSkins.Add(new InstalledSkin
                    {
                        CarId = skinInfo.CarId,
                        SkinName = skinInfo.SkinName,
                        TargetPath = targetDir
                    });
                    Log.Information("Installed skin: {CarId}/{SkinName}", skinInfo.CarId, skinInfo.SkinName);
                }

                // Copy any remaining texture files that weren't in cardesign folders
                CopySkinFiles(tempDir, aceContentFolder, textureFiles, textureMipsFiles);
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Skin installation failed for {Path}", archivePath);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static bool HasCardesignFiles(string archivePath)
    {
        try
        {
            using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(archivePath, new SharpCompress.Readers.ReaderOptions());
            return archive.Entries.Any(e => e.Key != null && e.Key.EndsWith(".cardesign", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static (string CarId, string SkinName) ParseCardesign(string cardesignPath)
    {
        try
        {
            // Try to parse as JSON first
            var content = File.ReadAllText(cardesignPath);
            if (content.TrimStart().StartsWith("{"))
            {
                var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                var carId = root.TryGetProperty("carId", out var c) ? c.GetString() : "";
                var skinName = root.TryGetProperty("skinName", out var s) ? s.GetString() : "";
                if (!string.IsNullOrEmpty(carId) && !string.IsNullOrEmpty(skinName))
                    return (carId, skinName);
            }

            // Fallback: use parent folder name or file name
            var parentDir = Path.GetFileName(Path.GetDirectoryName(cardesignPath)) ?? "";
            var fileName = Path.GetFileNameWithoutExtension(cardesignPath);
            return (parentDir, fileName);
        }
        catch
        {
            // Binary format — extract strings
            var content = File.ReadAllBytes(cardesignPath);
            var strings = ExtractStrings(content);
            var carId = strings.Count > 0 ? strings[0] : "unknown";
            var skinName = strings.Count > 1 ? strings[1] : Path.GetFileNameWithoutExtension(cardesignPath);
            return (carId, skinName);
        }
    }

    private static List<string> ExtractStrings(byte[] data)
    {
        var strings = new List<string>();
        var current = new List<byte>();
        foreach (var b in data)
        {
            if (b >= 32 && b <= 126)
                current.Add(b);
            else if (current.Count > 2)
            {
                strings.Add(System.Text.Encoding.ASCII.GetString(current.ToArray()));
                current.Clear();
            }
            else
                current.Clear();
        }
        if (current.Count > 2)
            strings.Add(System.Text.Encoding.ASCII.GetString(current.ToArray()));
        return strings;
    }

    private static void CopySkinFiles(string srcDir, string destDir,
        string[] textureFiles, string[] textureMipsFiles)
    {
        var allTextureFiles = textureFiles.Concat(textureMipsFiles).ToList();

        // Build a mapping: filename without .texture/.texturemips → full path
        var textureMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in allTextureFiles)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!textureMap.ContainsKey(name))
                textureMap[name] = f;
        }

        foreach (var kvp in textureMap)
        {
            var destPath = Path.Combine(destDir, Path.GetFileName(kvp.Value));
            if (!File.Exists(destPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(kvp.Value, destPath, true);
            }
        }
    }
}

public class SkinInstallResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SkinName { get; set; }
    public string? TargetPath { get; set; }
    public int CardesignCount { get; set; }
    public int TextureCount { get; set; }
    public List<InstalledSkin> InstalledSkins { get; set; } = new();
}

public class InstalledSkin
{
    public string CarId { get; set; } = "";
    public string SkinName { get; set; } = "";
    public string TargetPath { get; set; } = "";
}
