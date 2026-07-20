using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EVO.ModManager.Core.Services.Interfaces;

namespace EVO.ModManager.App.ViewModels;

public partial class ConverterViewModel : ObservableObject
{
    private readonly IModConverterService _converterService;
    private readonly IArchiveService _archiveService;
    private readonly IGameDetectionService _gameDetection;

    public ConverterViewModel(
        IModConverterService converterService,
        IArchiveService archiveService,
        IGameDetectionService gameDetection)
    {
        _converterService = converterService;
        _archiveService = archiveService;
        _gameDetection = gameDetection;
    }

    [ObservableProperty]
    private bool _isSdkAvailable;

    [ObservableProperty]
    private bool _isEvoForgeAvailable;

    [ObservableProperty]
    private bool _canConvert;

    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private string _sourceFileName = string.Empty;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private double _conversionProgress;

    [ObservableProperty]
    private string _outputMessage = string.Empty;

    [ObservableProperty]
    private bool _hasOutput;

    [ObservableProperty]
    private ObservableCollection<string> _logLines = new();

    partial void OnOutputMessageChanged(string value)
    {
        HasOutput = !string.IsNullOrEmpty(value);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsSdkAvailable = _converterService.IsSdkAvailable;
        IsEvoForgeAvailable = _converterService.IsEvoForgeAvailable;
        CanConvert = _converterService.CanConvert;
        Log($"SDK available: {IsSdkAvailable}");
        Log($"EvoForge available: {IsEvoForgeAvailable}");
        Log($"Can convert: {CanConvert}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    public void SelectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select AC mod archive",
            Filter = "Supported archives (*.zip;*.7z;*.rar;*.tar.gz;*.tar;*.tgz)|*.zip;*.7z;*.rar;*.tar.gz;*.tar;*.tgz|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            SetFile(dialog.FileName);
    }

    public void SetFile(string filePath)
    {
        SourceFilePath = filePath;
        SourceFileName = Path.GetFileName(filePath);
        CanConvert = _converterService.CanConvert && !string.IsNullOrEmpty(filePath);
        Log($"Selected: {SourceFileName}");
        if (!_archiveService.IsSupportedArchive(filePath))
            Log("Warning: file type may not be supported");
    }

    [RelayCommand]
    public async Task ConvertAsync()
    {
        if (string.IsNullOrEmpty(SourceFilePath) || IsConverting) return;

        IsConverting = true;
        ConversionProgress = 0;
        OutputMessage = string.Empty;
        LogLines.Clear();
        Log("Starting conversion...");

        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Saved Games", "ACE", "mods");

        try
        {
            var progress = new Progress<double>(p =>
            {
                ConversionProgress = p * 100;
            });

            var result = await _converterService.ConvertAcModAsync(
                SourceFilePath, outputDir, progress);

            if (result.Success)
            {
                Log($"Success — Mod: {result.ModName}");
                Log($"Output: {result.OutputKspkgPath}");
                OutputMessage = $"Converted: {result.ModName}";
                ConversionProgress = 100;
            }
            else
            {
                Log($"Failed: {result.ErrorMessage}");
                OutputMessage = result.ErrorMessage ?? "Conversion failed";
            }
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
            OutputMessage = ex.Message;
        }
        finally
        {
            IsConverting = false;
        }
    }

    [RelayCommand]
    public void ClearLog()
    {
        LogLines.Clear();
        OutputMessage = string.Empty;
    }

    private void Log(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
