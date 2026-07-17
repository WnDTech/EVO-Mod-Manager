using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class ConverterView : System.Windows.Controls.UserControl
{
    public ConverterView(ConverterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (s, e) => await viewModel.LoadCommand.ExecuteAsync(null);
    }

    private void DropZone_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && DataContext is ConverterViewModel vm)
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (files.Length > 0)
                vm.SetFile(files[0]);
        }
    }
}