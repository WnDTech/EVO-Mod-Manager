using System.Windows;
using EVO.ModManager.Core.Models;
using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "EVO Mod Manager";
    }

    private void ModCard_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is Mod mod
            && DataContext is MainViewModel vm)
        {
            vm.SelectedMod = mod;
        }
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files.Length > 0 && DataContext is MainViewModel vm)
                await vm.HandleDroppedFilesAsync(files);
        }
    }
}
