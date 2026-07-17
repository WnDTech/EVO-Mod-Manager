using System.Net.Http.Headers;
using Serilog;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.Core.Services.Implementations;

public class UrlInstallService : IUrlInstallService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<UrlInstallService>();

    private readonly IGameDetectionService _gameDetection;
    private readonly IArchiveService _archiveService;
    private readonly IModConverterService _converterService;
    private readonly IModDiscoveryService _modDiscovery;
    private readonly IStorageLocationService _storageService;
    private readonly SkinInstallerService _skinInstaller;
    private readonly HttpClient _httpClient;

    public UrlInstallService(
        IGameDetectionService gameDetection,
        IArchiveService archiveService,
        IModConverterService converterService,
        IModDiscoveryService modDiscovery,
        IStorageLocationService storageService)
    {
        _gameDetection = gameDetection;
        _archiveService = archiveService;
        _converterService = converterService;
        _modDiscovery = modDiscovery;
        _storageService = storageService;
        _skinInstaller = new SkinInstallerService();

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("EVO-Mod-Manager", "1.0"));
    }

    public async Task<UrlInstallResult> InstallFromUrlAsync(string url,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var paths = _gameDetection.DetectAll();
        if (paths.ModsFolder == null)
        {
            return new UrlInstallResult
            {
                Success = false,
                Message = "ACE mods folder could not be determined. Please launch Assetto Corsa EVO at least once."
            };
        }

        var modsFolder = paths.ModsFolder;
        var downloadDir = Path.Combine(Path.GetTempPath(), "EVOMM", "url", Guid.NewGuid().ToString());
        Directory.CreateDirectory(downloadDir);

        try
        {
            var fileName = await DownloadFileAsync(url, downloadDir, progress, ct);
            var archivePath = Path.Combine(downloadDir, fileName);

            Log.Information("Downloaded {Url} -> {Path}", url, archivePath);

            var analysis = _archiveService.AnalyzeArchive(archivePath);

            if (analysis.IsAcMod)
            {
                Log.Information("AC mod detected from URL: {Url}", url);
                var convResult = await _converterService.ConvertAcModAsync(archivePath, modsFolder, progress, ct);
                return new UrlInstallResult
                {
                    Success = convResult.Success,
                    Message = convResult.ErrorMessage ?? (convResult.Success ? "Conversion completed" : "Conversion failed"),
                    ModName = convResult.ModName
                };
            }

            if (analysis.HasCardesign)
            {
                Log.Information("Skin mod detected from URL: {Url}", url);
                var aceContentFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Saved Games", "ACE", "content");
                Directory.CreateDirectory(aceContentFolder);

                var skinResult = await _skinInstaller.InstallSkinAsync(archivePath, aceContentFolder, ct);
                if (skinResult.Success)
                {
                    var count = skinResult.InstalledSkins.Count;
                    var msg = count > 0
                        ? $"Installed {count} skin(s) for {count} car(s)"
                        : $"Installed skin: {skinResult.SkinName}";
                    return new UrlInstallResult
                    {
                        Success = true,
                        Message = msg,
                        ModName = skinResult.SkinName
                    };
                }

                return new UrlInstallResult
                {
                    Success = false,
                    Message = $"Skin installation failed: {skinResult.ErrorMessage}",
                    ModName = skinResult.SkinName
                };
            }

            if (analysis.HasKspkg)
            {
                var modName = analysis.SuggestedModName ?? Path.GetFileNameWithoutExtension(fileName);
                var extractDir = Path.Combine(Path.GetTempPath(), "EVOMM", "url-extract", Guid.NewGuid().ToString());
                Directory.CreateDirectory(extractDir);

                try
                {
                    Log.Information("Extracting {Name} from URL archive: {Url}", modName, url);
                    await _archiveService.ExtractArchiveAsync(archivePath, extractDir, progress, ct);

                    foreach (var kspkgFile in Directory.GetFiles(extractDir, "*.kspkg", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(kspkgFile);

                        var storage = _storageService.GetDefault();
                        if (storage != null)
                        {
                            var storageModDir = Path.Combine(storage.Path, modName);
                            Directory.CreateDirectory(storageModDir);
                            var destPath = Path.Combine(storageModDir, fi.Name);
                            File.Copy(kspkgFile, destPath, overwrite: true);

                            _storageService.SymlinkMod(modsFolder, modName, storageModDir);

                            var manifest = new SidecarManifest
                            {
                                Name = modName,
                                Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
                                SourceUrl = url,
                                InstalledAt = DateTime.UtcNow.ToString("O")
                            };
                            _modDiscovery.WriteSidecarManifest(
                                Path.Combine(storageModDir, $"{modName}.evomanifest.json"), manifest);
                        }
                        else
                        {
                            var destPath = Path.Combine(modsFolder, fi.Name);
                            File.Copy(kspkgFile, destPath, overwrite: true);

                            var manifest = new SidecarManifest
                            {
                                Name = modName,
                                Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
                                SourceUrl = url,
                                InstalledAt = DateTime.UtcNow.ToString("O")
                            };
                            _modDiscovery.WriteSidecarManifest(
                                Path.Combine(modsFolder, $"{modName}.evomanifest.json"), manifest);
                        }

                        Log.Information("Installed {ModName} from URL: {Url}", modName, url);
                    }

                    return new UrlInstallResult
                    {
                        Success = true,
                        Message = $"Installed {modName}",
                        ModName = modName
                    };
                }
                finally
                {
                    try { Directory.Delete(extractDir, recursive: true); } catch { }
                }
            }

            if (fileName.EndsWith(".kspkg", StringComparison.OrdinalIgnoreCase))
            {
                var modName = Path.GetFileNameWithoutExtension(fileName);
                var fi = new FileInfo(archivePath);
                var destPath = Path.Combine(modsFolder, fi.Name);

                File.Copy(archivePath, destPath, overwrite: true);

                var manifest = new SidecarManifest
                {
                    Name = modName,
                    Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
                    SourceUrl = url,
                    InstalledAt = DateTime.UtcNow.ToString("O")
                };
                _modDiscovery.WriteSidecarManifest(
                    Path.Combine(modsFolder, $"{modName}.evomanifest.json"), manifest);

                Log.Information("Installed direct KSPKG from URL: {Url}", url);
                return new UrlInstallResult
                {
                    Success = true,
                    Message = $"Installed {modName}",
                    ModName = modName
                };
            }

            return new UrlInstallResult
            {
                Success = false,
                Message = $"No supported mod content found in {fileName}"
            };
        }
        catch (OperationCanceledException)
        {
            return new UrlInstallResult
            {
                Success = false,
                Message = "Download was cancelled"
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "URL install failed for {Url}", url);
            return new UrlInstallResult
            {
                Success = false,
                Message = $"Install failed: {ex.Message}"
            };
        }
        finally
        {
            try { Directory.Delete(downloadDir, recursive: true); } catch { }
        }
    }

    private async Task<string> DownloadFileAsync(string url, string destDir,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        var extension = GetExtensionFromResponse(response, url);
        var tempPath = Path.Combine(destDir, $"download{extension}");

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (contentLength.HasValue)
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                progress?.Report((double)totalRead / contentLength.Value);
            }
        }
        else
        {
            await contentStream.CopyToAsync(fileStream, ct);
        }

        var fileName = GetFileNameFromResponse(response, url);
        var finalPath = Path.Combine(destDir, fileName);
        File.Move(tempPath, finalPath);

        return fileName;
    }

    private static string GetFileNameFromResponse(HttpResponseMessage response, string url)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        if (disposition?.FileNameStar != null)
            return disposition.FileNameStar;
        if (disposition?.FileName != null)
            return disposition.FileName.Trim('"');

        var uri = new Uri(url);
        var lastSegment = uri.Segments[^1];
        var decoded = Uri.UnescapeDataString(lastSegment);
        if (!string.IsNullOrWhiteSpace(decoded))
            return decoded;

        return $"mod{GetExtensionFromResponse(response, url)}";
    }

    private static string GetExtensionFromResponse(HttpResponseMessage response, string url)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        return contentType?.ToLowerInvariant() switch
        {
            "application/zip" or "application/x-zip-compressed" => ".zip",
            "application/x-7z-compressed" => ".7z",
            "application/x-rar-compressed" => ".rar",
            _ => Path.GetExtension(new Uri(url).AbsolutePath) switch
            {
                ".zip" or ".7z" or ".rar" or ".kspkg" => Path.GetExtension(new Uri(url).AbsolutePath),
                _ => ".zip"
            }
        };
    }
}
