using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class ConflictViewModel : ObservableObject
{
    private readonly IConflictDetectionService _conflictService;
    private readonly IModDiscoveryService _modDiscovery;

    private string _modsFolder = string.Empty;
    private List<Mod> _mods = new();

    public ConflictViewModel(
        IConflictDetectionService conflictService,
        IModDiscoveryService modDiscovery)
    {
        _conflictService = conflictService;
        _modDiscovery = modDiscovery;
        Conflicts.CollectionChanged += OnConflictsCollectionChanged;
    }

    [ObservableProperty]
    private ObservableCollection<ModConflict> _conflicts = new();

    [ObservableProperty]
    private ModConflict? _selectedConflict;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasConflicts;

    private void OnConflictsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasConflicts = Conflicts.Count > 0;
    }

    public void SetModsFolder(string path) => _modsFolder = path;

    public void SetMods(List<Mod> mods) => _mods = mods;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || _mods.Count == 0) return;
        IsScanning = true;
        StatusText = "Scanning for conflicts...";

        try
        {
            var results = await Task.Run(() => _conflictService.DetectConflicts(_mods));
            Conflicts.Clear();
            foreach (var conflict in results)
                Conflicts.Add(conflict);

            ConflictCount = Conflicts.Count(c => c.Resolution == ConflictResolution.Unresolved);
            StatusText = $"Scan complete — {Conflicts.Count} conflict(s) detected";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void ResolveKeepA(ModConflict conflict)
    {
        if (conflict == null) return;
        _conflictService.ResolveConflict(conflict, ConflictResolution.Winner, conflict.ModIdA);
        RefreshConflictDisplay();
        StatusText = $"Resolved: keeping {conflict.ModIdA}";
    }

    [RelayCommand]
    private void ResolveKeepB(ModConflict conflict)
    {
        if (conflict == null) return;
        _conflictService.ResolveConflict(conflict, ConflictResolution.Winner, conflict.ModIdB);
        RefreshConflictDisplay();
        StatusText = $"Resolved: keeping {conflict.ModIdB}";
    }

    [RelayCommand]
    private void IgnoreConflict(ModConflict conflict)
    {
        if (conflict == null) return;
        _conflictService.ResolveConflict(conflict, ConflictResolution.Ignored, null);
        RefreshConflictDisplay();
        StatusText = "Conflict ignored";
    }

    private void RefreshConflictDisplay()
    {
        var snapshot = Conflicts.ToList();
        Conflicts.Clear();
        foreach (var c in snapshot)
            Conflicts.Add(c);

        ConflictCount = Conflicts.Count(c => c.Resolution == ConflictResolution.Unresolved);
    }
}
