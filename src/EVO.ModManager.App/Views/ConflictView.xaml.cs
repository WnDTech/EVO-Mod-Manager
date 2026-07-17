using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class ConflictView : System.Windows.Controls.UserControl
{
    public ConflictView(ConflictViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
