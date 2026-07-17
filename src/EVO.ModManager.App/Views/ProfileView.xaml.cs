using EVO.ModManager.App.ViewModels;

namespace EVO.ModManager.App.Views;

public partial class ProfileView : System.Windows.Controls.UserControl
{
    public ProfileView(ProfileViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (s, e) => viewModel.LoadProfilesCommand.Execute(null);
    }
}
