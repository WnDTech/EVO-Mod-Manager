using System.Windows.Controls;
using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (s, e) => viewModel.LoadCommand.Execute(null);
    }
}
