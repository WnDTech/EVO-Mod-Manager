using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IStorageLocationService _storageService;
    private readonly IGameDetectionService _gameDetection;
    private readonly ILiveryLabService _liveryLabService;
    private readonly IEditorService _editorService;
    private readonly IModConverterService _converterService;
    private readonly IBackupService _backupService;
    private readonly ICollectionService _collectionService;
    private readonly IModDiscoveryService _modDiscovery;

    private string _modsFolder = string.Empty;

    public SettingsViewModel(
        IStorageLocationService storageService,
        IGameDetectionService gameDetection,
        ILiveryLabService liveryLabService,
        IEditorService editorService,
        IModConverterService converterService,
        IBackupService backupService,
        ICollectionService collectionService,
        IModDiscoveryService modDiscovery)
    {
        _storageService = storageService;
        _gameDetection = gameDetection;
        _liveryLabService = liveryLabService;
        _editorService = editorService;
        _converterService = converterService;
        _backupService = backupService;
        _collectionService = collectionService;
        _modDiscovery = modDiscovery;
    }

    public void SetModsFolder(string path) => _modsFolder = path;

    [ObservableProperty]
    private ObservableCollection<StorageLocationViewModel> _storageLocations = new();

    [ObservableProperty]
    private string _gamePath = "Detecting...";

    [ObservableProperty]
    private string _acePath = "Detecting...";

    [ObservableProperty]
    private string _sdkStatus = "Checking...";

    [ObservableProperty]
    private string _liveryLabStatus = "Checking...";

    [ObservableProperty]
    private string _newLocationName = string.Empty;

    [ObservableProperty]
    private string _newLocationPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _backupStatus = string.Empty;

    [ObservableProperty]
    private string _collectionStatus = string.Empty;

    [RelayCommand]
    public void Load()
    {
        var paths = _gameDetection.DetectAll();
        GamePath = paths.GamePath ?? "Not found";
        AcePath = paths.AceUserFolder ?? "Not found";
        SdkStatus = _editorService.IsEditorAvailable ? $"Available at {_editorService.EditorPath}" : "Not installed";
        LiveryLabStatus = _liveryLabService.IsInstalled ? $"Available at {_liveryLabService.LiveryLabPath}" : "Not installed — will auto-download on first skin mod";

        RefreshStorageLocations();
    }

    [RelayCommand]
    public void RefreshStorageLocations()
    {
        StorageLocations.Clear();
        foreach (var loc in _storageService.GetAll())
        {
            var freeBytes = _storageService.GetFreeSpace(loc.Path);
            var freeText = freeBytes switch
            {
                < 0 => "Unknown",
                < 1024L * 1024 => $"{freeBytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{freeBytes / (1024.0 * 1024):F1} MB",
                _ => $"{freeBytes / (1024.0 * 1024 * 1024):F2} GB"
            };

            StorageLocations.Add(new StorageLocationViewModel
            {
                Id = loc.Id,
                Name = loc.Name,
                Path = loc.Path,
                IsDefault = loc.IsDefault,
                FreeSpace = freeText
            });
        }
    }

    [RelayCommand]
    public void BrowseLocation()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select storage location for mod files",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            NewLocationPath = dialog.SelectedPath;
    }

    [RelayCommand]
    public void AddLocation()
    {
        if (string.IsNullOrWhiteSpace(NewLocationName))
        {
            StatusMessage = "Please enter a name for the storage location";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewLocationPath) || !Directory.Exists(NewLocationPath))
        {
            StatusMessage = "Please select a valid folder path";
            return;
        }

        var location = new StorageLocation
        {
            Name = NewLocationName,
            Path = NewLocationPath,
            IsActive = true,
            IsDefault = !_storageService.GetAll().Any()
        };

        _storageService.Add(location);
        RefreshStorageLocations();
        NewLocationName = string.Empty;
        NewLocationPath = string.Empty;
        StatusMessage = $"Added storage location: {location.Name}";
    }

    [RelayCommand]
    public void RemoveLocation(string id)
    {
        _storageService.Remove(id);
        RefreshStorageLocations();
        StatusMessage = "Storage location removed";
    }

    [RelayCommand]
    public void SetDefaultLocation(string id)
    {
        _storageService.SetDefault(id);
        RefreshStorageLocations();
        StatusMessage = "Default storage location updated";
    }

    [RelayCommand]
    public async Task DownloadLiveryLabAsync()
    {
        StatusMessage = "Downloading LiveryLab...";
        await _liveryLabService.AutoDownloadAsync();
        LiveryLabStatus = _liveryLabService.IsInstalled
            ? $"Available at {_liveryLabService.LiveryLabPath}"
            : "Download failed";
        StatusMessage = _liveryLabService.IsInstalled ? "LiveryLab installed" : "LiveryLab download failed";
    }

    [RelayCommand]
    public async Task BackupAllAsync()
    {
        if (string.IsNullOrEmpty(_modsFolder) || !Directory.Exists(_modsFolder))
        {
            BackupStatus = "Mods folder not available";
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EVO Mod Manager", "backups");
        Directory.CreateDirectory(backupDir);
        var destZip = Path.Combine(backupDir, $"mods-backup-{timestamp}.zip");

        BackupStatus = "Creating backup...";
        try
        {
            var progress = new Progress<double>(p => BackupStatus = $"Backing up... {p * 100:F0}%");
            await _backupService.BackupFullAsync(_modsFolder, destZip, progress);
            BackupStatus = $"Backup completed: {Path.GetFileName(destZip)}";
        }
        catch (Exception ex)
        {
            BackupStatus = $"Backup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportCollectionAsync()
    {
        if (string.IsNullOrEmpty(_modsFolder))
        {
            CollectionStatus = "Mods folder not available";
            return;
        }

        var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Export Mod Collection",
            Filter = "Collection files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"mod-collection-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        CollectionStatus = "Exporting collection...";
        try
        {
            var mods = await _modDiscovery.ScanModsFolderAsync(_modsFolder);
            await _collectionService.ExportCollectionAsync(mods, dialog.FileName);
            CollectionStatus = $"Exported {mods.Count} mods to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            CollectionStatus = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportCollectionAsync()
    {
        if (string.IsNullOrEmpty(_modsFolder))
        {
            CollectionStatus = "Mods folder not available";
            return;
        }

        var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Import Mod Collection",
            Filter = "Collection files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        CollectionStatus = "Importing collection...";
        try
        {
            var result = await _collectionService.ImportCollectionAsync(dialog.FileName, _modsFolder);
            var msg = $"Found {result.AlreadyInstalled.Count} already installed, {result.ImportedMods.Count} new, {result.MissingFromDisk.Count} missing";
            CollectionStatus = msg;
            System.Windows.MessageBox.Show(msg, "Collection Import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            CollectionStatus = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportCollectionWithFilesAsync()
    {
        if (string.IsNullOrEmpty(_modsFolder))
        {
            CollectionStatus = "Mods folder not available";
            return;
        }

        var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Export Mod Collection with Files",
            Filter = "Zip archives (*.zip)|*.zip",
            FileName = $"mod-collection-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        CollectionStatus = "Exporting collection with files...";
        try
        {
            var mods = await _modDiscovery.ScanModsFolderAsync(_modsFolder);
            await _collectionService.ExportCollectionWithFilesAsync(mods, dialog.FileName, _modsFolder);
            CollectionStatus = $"Exported {mods.Count} mods to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            CollectionStatus = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportCollectionWithFilesAsync()
    {
        var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Import Mod Collection with Files",
            Filter = "Zip archives (*.zip)|*.zip",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        if (string.IsNullOrEmpty(_modsFolder))
        {
            CollectionStatus = "Mods folder not available";
            return;
        }

        CollectionStatus = "Importing collection with files...";
        try
        {
            var progress = new Progress<double>(p => CollectionStatus = $"Importing... {p * 100:F0}%");
            await _collectionService.ImportCollectionWithFilesAsync(dialog.FileName, _modsFolder, progress);
            CollectionStatus = "Collection imported successfully";
            System.Windows.MessageBox.Show("Mods have been extracted to the mods folder.", "Collection Import",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            CollectionStatus = $"Import failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RestoreBackupAsync()
    {
        var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Select a backup file to restore",
            Filter = "Backup files (*.zip)|*.zip",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var backupZip = dialog.FileName;
        if (string.IsNullOrEmpty(_modsFolder))
        {
            BackupStatus = "Mods folder not available";
            return;
        }

        BackupStatus = "Restoring from backup...";
        try
        {
            var progress = new Progress<double>(p => BackupStatus = $"Restoring... {p * 100:F0}%");
            await _backupService.RestoreAsync(backupZip, _modsFolder, progress: progress);
            BackupStatus = $"Restore completed from: {Path.GetFileName(backupZip)}";
        }
        catch (Exception ex)
        {
            BackupStatus = $"Restore failed: {ex.Message}";
        }
    }
}

public partial class StorageLocationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private string _freeSpace = "Unknown";
}

