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

        Log.Information("EVO Mod Manager v{Version} starting", AppVersion);

        DispatcherUnhandledException += (s, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled UI exception");
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nLogs: {Path.Combine(appData, "logs")}",
                "EVO Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Fatal(ex, "AppDomain exception");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Fatal(e.Exception, "Task exception");
            e.SetObserved();
        };
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
        var browseVm = _serviceProvider.GetRequiredService<BrowseViewModel>();
        browseVm.SetModsFolder(mainVm.ModsFolderForBrowse);

        mainWindow.Loaded += async (s, args) =>
        {
            if (mainVm.InitializeCommand.CanExecute(null))
                await mainVm.InitializeCommand.ExecuteAsync(null);
        };

        mainWindow.Show();

        _ = CheckForUpdatesAsync();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void RegisterServices(ServiceCollection services)
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

        services.AddTransient<SettingsView>();
        services.AddTransient<BrowseView>();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateService = new UpdateCheckService();
            var info = await updateService.CheckForUpdateAsync(AppVersion);
            if (info.HasUpdate && info.DownloadUrl != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var result = System.Windows.MessageBox.Show(
                        $"Version {info.LatestVersion} available.\n\nDownload now?",
                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        { UseShellExecute = true, FileName = info.DownloadUrl });
                });
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Update check failed"); }
    }
}
