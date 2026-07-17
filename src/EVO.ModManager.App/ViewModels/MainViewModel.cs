using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGameDetectionService _gameDetection;
    private readonly IModDiscoveryService _modDiscovery;
    private readonly IArchiveService _archiveService;
    private readonly IStorageLocationService _storageService;
    private readonly IEditorService _editorService;
    private readonly ILiveryLabService _liveryLabService;
    private readonly IModBrowserService _browserService;
    private readonly IModConverterService _converterService;
    private readonly IBackupService _backupService;
    private readonly IProfileService _profileService;
    private readonly IConflictDetectionService _conflictService;

    private string _modsFolder = string.Empty;
    private string _gamePath = string.Empty;

    public MainViewModel(
        IGameDetectionService gameDetection,
        IModDiscoveryService modDiscovery,
        IArchiveService archiveService,
        IStorageLocationService storageService,
        IEditorService editorService,
        ILiveryLabService liveryLabService,
        IModBrowserService browserService,
        IModConverterService converterService,
        IBackupService backupService,
        IProfileService profileService,
        IConflictDetectionService conflictService)
    {
        _gameDetection = gameDetection;
        _modDiscovery = modDiscovery;
        _archiveService = archiveService;
        _storageService = storageService;
        _editorService = editorService;
        _liveryLabService = liveryLabService;
        _browserService = browserService;
        _converterService = converterService;
        _backupService = backupService;
        _profileService = profileService;
        _conflictService = conflictService;

        _selectedNavItem = NavItems[0];
    }

    [ObservableProperty]
    private ObservableCollection<Mod> _mods = new();

    [ObservableProperty]
    private Mod? _selectedMod;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _modCount;

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private string _gamePathDisplay = "Not detected";

    [ObservableProperty]
    private string _modsFolderDisplay = "Not set";

    [ObservableProperty]
    private bool _isEditorAvailable;

    [ObservableProperty]
    private bool _isLiveryLabAvailable;

    [ObservableProperty]
    private bool _isConverterAvailable;

    [ObservableProperty]
    private ModType _selectedModType = ModType.Unknown;

    public List<ModType> ModTypeOptions { get; } = new()
    {
        ModType.Unknown, ModType.Car, ModType.Track, ModType.Skin,
        ModType.Sound, ModType.App, ModType.Misc
    };

    [ObservableProperty]
    private string _searchText = string.Empty;

    public List<string> NavItems { get; } = new()
    {
        "Mods", "Browse", "Profiles", "Converter", "Settings"
    };

    [ObservableProperty]
    private string _selectedNavItem;

    partial void OnSelectedModTypeChanged(ModType value) => OnPropertyChanged(nameof(FilteredMods));

    partial void OnSelectedNavItemChanged(string value)
    {
        OnPropertyChanged(nameof(IsModsViewVisible));
        OnPropertyChanged(nameof(IsSettingsViewVisible));
        OnPropertyChanged(nameof(IsBrowseViewVisible));
    }

    public bool IsModsViewVisible => SelectedNavItem == "Mods";
    public bool IsSettingsViewVisible => SelectedNavItem == "Settings";
    public bool IsBrowseViewVisible => SelectedNavItem == "Browse";

    public System.Windows.Controls.UserControl? SettingsView { get; set; }
    public System.Windows.Controls.UserControl? BrowseView { get; set; }
    public string ModsFolderForBrowse => _modsFolder;

    public async Task HandleDroppedFilesAsync(string[] files)
    {
        var archives = files.Where(f => _archiveService.IsSupportedArchive(f)).ToList();
        if (archives.Count == 0)
        {
            StatusText = "No supported archive files found";
            return;
        }

        foreach (var archive in archives)
        {
            await InstallFromArchiveAsync(archive);
        }
    }

    [RelayCommand]
    public async Task InstallArchiveAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select mod archive",
            Filter = "Supported archives (*.zip;*.7z;*.rar;*.tar.gz;*.tar;*.tgz)|*.zip;*.7z;*.rar;*.tar.gz;*.tar;*.tgz|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                await InstallFromArchiveAsync(file);
        }
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            StatusText = "Detecting game installation...";

            var paths = _gameDetection.DetectAll();

            _gamePath = paths.GamePath ?? string.Empty;
            GamePathDisplay = paths.GamePath ?? "Not found";
            ModsFolderDisplay = paths.ModsFolder ?? "Not set";
            _modsFolder = paths.ModsFolder ?? string.Empty;

            IsEditorAvailable = _editorService.IsEditorAvailable;
            IsLiveryLabAvailable = _liveryLabService.IsInstalled;
            IsConverterAvailable = _converterService.IsSdkAvailable;

            if (!string.IsNullOrEmpty(_modsFolder))
                await RefreshModsAsync();

            StatusText = $"Ready — {ModCount} mods, {ConflictCount} conflicts";
        }
        catch (Exception ex)
        {
            StatusText = $"Error during initialization: {ex.Message}";
            Serilog.Log.Error(ex, "Initialization failed");
        }
    }

    [RelayCommand]
    public async Task RefreshModsAsync()
    {
        if (string.IsNullOrEmpty(_modsFolder)) return;

        StatusText = "Scanning mods...";
        var mods = await _modDiscovery.ScanModsFolderAsync(_modsFolder);

        Mods.Clear();
        foreach (var mod in mods.OrderByDescending(m => m.InstalledAt))
            Mods.Add(mod);

        ModCount = Mods.Count;
        ConflictCount = 0;
    }

    private async Task InstallFromArchiveAsync(string archivePath)
    {
        StatusText = $"Analyzing {Path.GetFileName(archivePath)}...";

        var analysis = _archiveService.AnalyzeArchive(archivePath);

        if (analysis.HasCardesign)
        {
            if (!IsLiveryLabAvailable)
            {
                StatusText = "LiveryLab required for skin mods. Installing LiveryLab...";
                await _liveryLabService.AutoDownloadAsync();
                IsLiveryLabAvailable = true;
            }
            _liveryLabService.LaunchWithZip(archivePath);
            StatusText = "LiveryLab launched for skin import";
            return;
        }

        // AC mod detected — route to converter
        if (analysis.IsAcMod)
        {
            StatusText = "AC mod detected — launching converter...";
            var convResult = await _converterService.ConvertAcModAsync(archivePath, "");
            if (convResult.Success)
            {
                StatusText = convResult.ErrorMessage ?? "Converter launched";
            }
            else
            {
                StatusText = convResult.ErrorMessage ?? "No converter available";
            }
            return;
        }

        if (!analysis.HasKspkg)
        {
            StatusText = $"No .kspkg files found in {Path.GetFileName(archivePath)}";
            return;
        }

        var modName = analysis.SuggestedModName ?? Path.GetFileNameWithoutExtension(archivePath);

        var tempDir = Path.Combine(Path.GetTempPath(), "EVOMM", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            StatusText = $"Extracting {modName}...";
            await _archiveService.ExtractArchiveAsync(archivePath, tempDir);

            foreach (var kspkgFile in Directory.GetFiles(tempDir, "*.kspkg", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(kspkgFile);
                var destName = fi.Name;

                // Check if storage location is default
                var storage = _storageService.GetDefault();

                if (storage != null)
                {
                    var storageModDir = Path.Combine(storage.Path, modName);
                    Directory.CreateDirectory(storageModDir);
                    var destPath = Path.Combine(storageModDir, destName);
                    File.Copy(kspkgFile, destPath, overwrite: true);

                    _storageService.SymlinkMod(_modsFolder, modName, storageModDir);

                    var manifest = new SidecarManifest
                    {
                        Name = modName,
                        Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
                        InstalledAt = DateTime.UtcNow.ToString("O")
                    };
                    _modDiscovery.WriteSidecarManifest(
                        Path.Combine(storageModDir, $"{modName}.evomanifest.json"), manifest);
                }
                else
                {
                    var destPath = Path.Combine(_modsFolder, destName);
                    File.Copy(kspkgFile, destPath, overwrite: true);

                    var manifest = new SidecarManifest
                    {
                        Name = modName,
                        Type = _modDiscovery.ClassifyMod(modName, null).ToString(),
                        InstalledAt = DateTime.UtcNow.ToString("O")
                    };
                    _modDiscovery.WriteSidecarManifest(
                        Path.Combine(_modsFolder, $"{modName}.evomanifest.json"), manifest);
                }
            }

            await RefreshModsAsync();
            StatusText = $"Installed {modName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [RelayCommand]
    public void ToggleMod(Mod mod)
    {
        if (string.IsNullOrEmpty(_modsFolder)) return;

        if (mod.IsEnabled)
        {
            var disabledDir = Path.Combine(_modsFolder, "_disabled");
            Directory.CreateDirectory(disabledDir);

            if (mod.IsSymlinked && !string.IsNullOrEmpty(mod.SymlinkTarget))
            {
                _storageService.RemoveSymlink(_modsFolder, Path.GetFileNameWithoutExtension(mod.FileName));
            }
            else
            {
                var sourcePath = Path.Combine(_modsFolder, mod.FileName);
                if (File.Exists(sourcePath))
                {
                    var destPath = Path.Combine(disabledDir, mod.FileName);
                    File.Move(sourcePath, destPath);
                    mod.DisabledPath = destPath;
                }
            }

            mod.IsEnabled = false;
        }
        else
        {
            if (mod.IsSymlinked && !string.IsNullOrEmpty(mod.SymlinkTarget))
            {
                _storageService.SymlinkMod(_modsFolder,
                    Path.GetFileNameWithoutExtension(mod.FileName),
                    mod.SymlinkTarget);
            }
            else if (!string.IsNullOrEmpty(mod.DisabledPath) && File.Exists(mod.DisabledPath))
            {
                var destPath = Path.Combine(_modsFolder, mod.FileName);
                File.Move(mod.DisabledPath, destPath);
                mod.DisabledPath = null;
            }

            mod.IsEnabled = true;
        }

        ModCount = Mods.Count(m => m.IsEnabled);
    }

    [RelayCommand]
    public void DeleteMod(Mod mod)
    {
        if (string.IsNullOrEmpty(_modsFolder)) return;

        if (mod.IsSymlinked)
            _storageService.RemoveSymlink(_modsFolder, Path.GetFileNameWithoutExtension(mod.FileName));

        var filePath = mod.IsEnabled
            ? Path.Combine(_modsFolder, mod.FileName)
            : mod.DisabledPath;

        if (filePath != null && File.Exists(filePath))
        {
            File.Delete(filePath);
            var sidecarPath = Path.Combine(
                Path.GetDirectoryName(filePath)!,
                $"{Path.GetFileNameWithoutExtension(mod.FileName)}.evomanifest.json");
            if (File.Exists(sidecarPath))
                File.Delete(sidecarPath);
        }

        Mods.Remove(mod);
        ModCount = Mods.Count;
    }

    [RelayCommand]
    public void LaunchGame()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = "steam://rungameid/3058630"
        });
    }

    [RelayCommand]
    public void LaunchEditor() => _editorService.LaunchEditor();

    [RelayCommand]
    public void NavigateToMods() => SelectedNavItem = "Mods";

    [RelayCommand]
    public void OpenSettings() => SelectedNavItem = "Settings";

    [RelayCommand]
    public void OpenProfiles() => StatusText = "Profiles view coming soon";

    [RelayCommand]
    public void OpenBrowse() => SelectedNavItem = "Browse";

    public IEnumerable<Mod> FilteredMods
    {
        get
        {
            var filtered = SelectedModType == ModType.Unknown
                ? Mods
                : Mods.Where(m => m.ModType == SelectedModType);
            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(m =>
                    m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (m.Author?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true));
            return filtered;
        }
    }
}






