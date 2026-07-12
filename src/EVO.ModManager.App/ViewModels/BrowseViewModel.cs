using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class BrowseViewModel : ObservableObject
{
    private readonly IModBrowserService _browserService;
    private readonly IArchiveService _archiveService;
    private readonly IModDiscoveryService _modDiscovery;
    private readonly IStorageLocationService _storageService;
    private readonly ILiveryLabService _liveryLabService;

    private string _modsFolder = string.Empty;

    public BrowseViewModel(
        IModBrowserService browserService,
        IArchiveService archiveService,
        IModDiscoveryService modDiscovery,
        IStorageLocationService storageService,
        ILiveryLabService liveryLabService)
    {
        _browserService = browserService;
        _archiveService = archiveService;
        _modDiscovery = modDiscovery;
        _storageService = storageService;
        _liveryLabService = liveryLabService;
    }

    [ObservableProperty]
    private ObservableCollection<DownloadableMod> _mods = new();

    [ObservableProperty]
    private ObservableCollection<ModSource> _sources = new();

    [ObservableProperty]
    private DownloadableMod? _selectedMod;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private ModType _selectedCategory = ModType.Unknown;

    [ObservableProperty]
    private ModSource? _selectedSource;

    public List<ModType> Categories { get; } = new()
    {
        ModType.Unknown, ModType.Car, ModType.Track, ModType.Skin,
        ModType.Sound, ModType.App, ModType.Misc
    };

    partial void OnSelectedSourceChanged(ModSource? value)
    {
        if (value != null) _ = FetchModsAsync();
    }

    partial void OnSelectedCategoryChanged(ModType value)
    {
        _ = FetchModsAsync();
    }

    public void SetModsFolder(string path) => _modsFolder = path;

    [RelayCommand]
    public async Task LoadSourcesAsync()
    {
        var sources = await _browserService.GetSourcesAsync();
        Sources.Clear();
        foreach (var s in sources)
            Sources.Add(s);
        SelectedSource = Sources.FirstOrDefault();
    }

    [RelayCommand]
    public async Task FetchModsAsync()
    {
        if (IsLoading || SelectedSource == null) return;
        IsLoading = true;
        StatusText = $"Fetching mods from {SelectedSource.Name}...";

        try
        {
            var mods = await _browserService.FetchModListAsync(
                SelectedSource.Id,
                SelectedCategory == ModType.Unknown ? null : SelectedCategory);

            Mods.Clear();
            foreach (var mod in mods)
                Mods.Add(mod);

            StatusText = $"Found {mods.Count} mods from {SelectedSource.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DownloadSelectedAsync()
    {
        if (SelectedMod == null || IsDownloading) return;

        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = $"Downloading {SelectedMod.Title}...";

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", "downloads");
            Directory.CreateDirectory(tempDir);

            var progress = new Progress<double>(p => DownloadProgress = p);
            var archivePath = await _browserService.DownloadModAsync(
                SelectedMod, tempDir, progress);

            StatusText = "Analyzing downloaded mod...";
            var analysis = _archiveService.AnalyzeArchive(archivePath);

            if (analysis.HasCardesign)
            {
                if (!_liveryLabService.IsInstalled)
                {
                    StatusText = "Downloading LiveryLab for skin import...";
                    await _liveryLabService.AutoDownloadAsync();
                }
                _liveryLabService.LaunchWithZip(archivePath);
                StatusText = "LiveryLab launched for skin import";
                return;
            }

            if (!analysis.HasKspkg)
            {
                StatusText = "No .kspkg files found in downloaded mod";
                return;
            }

            var extractDir = Path.Combine(tempDir, "extracted");
            await _archiveService.ExtractArchiveAsync(archivePath, extractDir);

            foreach (var kspkg in Directory.GetFiles(extractDir, "*.kspkg", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(kspkg);
                var storage = _storageService.GetDefault();

                if (storage != null)
                {
                    var modDir = Path.Combine(storage.Path, fi.Name);
                    Directory.CreateDirectory(modDir);
                    File.Copy(kspkg, Path.Combine(modDir, fi.Name), overwrite: true);
                    _storageService.SymlinkMod(_modsFolder, Path.GetFileNameWithoutExtension(fi.Name), modDir);
                }
                else
                {
                    File.Copy(kspkg, Path.Combine(_modsFolder, fi.Name), overwrite: true);
                }

                var manifestPath = Path.Combine(
                    storage?.Path ?? _modsFolder,
                    $"{Path.GetFileNameWithoutExtension(fi.Name)}.evomanifest.json");
                _modDiscovery.WriteSidecarManifest(manifestPath, new SidecarManifest
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = SelectedMod.Title,
                    Type = SelectedMod.ModType.ToString(),
                    Version = SelectedMod.Title.Contains(" v") ? SelectedMod.Title.Split(" v").Last() : null,
                    Author = SelectedMod.Author,
                    Description = SelectedMod.Description?.Length > 500
                        ? SelectedMod.Description[..500] : SelectedMod.Description,
                    SourceUrl = SelectedMod.DownloadUrl,
                    SourceModId = SelectedMod.ResourceId,
                    InstalledAt = DateTime.UtcNow.ToString("O")
                });
            }

            StatusText = $"Installed: {SelectedMod.Title}";

            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
