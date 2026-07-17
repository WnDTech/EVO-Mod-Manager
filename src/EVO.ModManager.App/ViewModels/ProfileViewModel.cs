using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Models;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IModDiscoveryService _modDiscovery;
    private string _modsFolder = string.Empty;

    public ProfileViewModel(
        IProfileService profileService,
        IModDiscoveryService modDiscovery)
    {
        _profileService = profileService;
        _modDiscovery = modDiscovery;
    }

    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private Profile? _selectedProfile;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileDescription = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isActivating;

    public void SetModsFolder(string path) => _modsFolder = path;

    [RelayCommand]
    public void LoadProfiles()
    {
        var all = _profileService.GetAll();
        Profiles.Clear();
        foreach (var p in all)
            Profiles.Add(p);
        StatusText = $"{Profiles.Count} profile(s) loaded";
    }

    [RelayCommand]
    public void CreateProfile()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName))
        {
            StatusText = "Please enter a profile name";
            return;
        }

        var profile = new Profile
        {
            Name = NewProfileName.Trim(),
            Description = string.IsNullOrWhiteSpace(NewProfileDescription)
                ? null
                : NewProfileDescription.Trim()
        };

        _profileService.Create(profile);
        Profiles.Add(profile);
        SelectedProfile = profile;
        NewProfileName = string.Empty;
        NewProfileDescription = string.Empty;
        StatusText = $"Profile '{profile.Name}' created";
    }

    [RelayCommand]
    public async Task ActivateProfileAsync()
    {
        if (SelectedProfile == null)
        {
            StatusText = "No profile selected";
            return;
        }

        if (string.IsNullOrEmpty(_modsFolder))
        {
            StatusText = "Mods folder not set";
            return;
        }

        IsActivating = true;
        try
        {
            await _profileService.ActivateAsync(SelectedProfile.Id, _modsFolder);
            StatusText = $"Profile '{SelectedProfile.Name}' activated";
        }
        catch (Exception ex)
        {
            StatusText = $"Activation failed: {ex.Message}";
        }
        finally
        {
            IsActivating = false;
        }
    }

    [RelayCommand]
    public void DeleteProfile()
    {
        if (SelectedProfile == null)
        {
            StatusText = "No profile selected";
            return;
        }

        var name = SelectedProfile.Name;
        _profileService.Delete(SelectedProfile.Id);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        StatusText = $"Profile '{name}' deleted";
    }
}
