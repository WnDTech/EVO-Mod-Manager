using System.Windows.Controls;
using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class BrowseView : System.Windows.Controls.UserControl
{
    public BrowseView(BrowseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (s, e) =>
        {
            if (viewModel.FetchModsCommand.CanExecute(null))
                await viewModel.FetchModsCommand.ExecuteAsync(null);
        };
    }
}
