using System.IO;
using System.Windows;
using EVO.ModManager.App.Services;
using EVO.ModManager.App.ViewModels;
using EVO.ModManager.App.Views;
using EVO.ModManager.Core.Data;
using EVO.ModManager.Core.Services.Implementations;
using EVO.ModManager.Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EVO.ModManager.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private const string AppVersion = "1.0.0";

    public App()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EVO Mod Manager");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(appData, "logs", "evomm-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .WriteTo.Debug()
            .CreateLogger();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        RegisterServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Create MainWindow manually so DataContext is set before window is shown
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
        var mainWindow = new Views.MainWindow();
        mainWindow.DataContext = mainVm;
        Current.MainWindow = mainWindow;

                mainVm.SettingsView = _serviceProvider.GetRequiredService<SettingsView>();
        mainVm.BrowseView = _serviceProvider.GetRequiredService<BrowseView>();
        mainVm.ProfileView = _serviceProvider.GetRequiredService<ProfileView>();
        mainVm.ConverterView = _serviceProvider.GetRequiredService<ConverterView>();
        mainVm.ConflictView = _serviceProvider.GetRequiredService<ConflictView>();
        var browseVm = _serviceProvider.GetRequiredService<BrowseViewModel>();
        browseVm.SetModsFolder(mainVm.ModsFolderForBrowse);
        var settingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        settingsVm.SetModsFolder(mainVm.ModsFolderForBrowse);
        var profileVm = _serviceProvider.GetRequiredService<ProfileViewModel>();
        profileVm.SetModsFolder(mainVm.ModsFolderForBrowse);
        var conflictVm = _serviceProvider.GetRequiredService<ConflictViewModel>();
        conflictVm.SetModsFolder(mainVm.ModsFolderForBrowse);
        conflictVm.SetMods(mainVm.Mods.ToList());

        mainWindow.Loaded += async (s, args) =>
        {
            if (mainVm.InitializeCommand.CanExecute(null))
                await mainVm.InitializeCommand.ExecuteAsync(null);
        };

        mainWindow.Show();

        _ = CheckForUpdatesAsync();

        base.OnStartup(e);
    }

    private void RegisterServices(IServiceCollection services)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EVO Mod Manager");
        services.AddSingleton(new DatabaseContext(Path.Combine(appData, "evomm.db")));
        services.AddSingleton<ModRepository>();
        services.AddSingleton<SettingsRepository>();

        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IModDiscoveryService, ModDiscoveryService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<IStorageLocationService, StorageLocationService>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IConflictDetectionService, ConflictDetectionService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IModBrowserService, ModBrowserService>();
        services.AddSingleton<IModConverterService, ModConverterService>();
        services.AddSingleton<IEditorService, EditorService>();
        services.AddSingleton<ILiveryLabService, LiveryLabService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<ProfileViewModel>();
        services.AddSingleton<ConverterViewModel>();
        services.AddSingleton<ConflictViewModel>();

        services.AddTransient<SettingsView>();

        services.AddTransient<BrowseView>();
        services.AddTransient<ProfileView>();
        services.AddTransient<ConverterView>();
        services.AddTransient<ConflictView>();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateService = new UpdateCheckService();
            var info = await updateService.CheckForUpdateAsync(AppVersion);
            if (info.HasUpdate && info.DownloadUrl != null)
            {
                Log.Information("Update available: {Version} at {Url}", info.LatestVersion, info.DownloadUrl);
                // Future: show update notification
            }
            else
            {
                Log.Information("No update available (current: {Version})", AppVersion);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
        }
    }
}


