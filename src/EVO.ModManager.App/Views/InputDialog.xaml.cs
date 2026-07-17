using System.Windows;
using System.Windows.Input;

namespace EVO.ModManager.App.Views;

public partial class InputDialog : Window
{
    public string? InputText => UrlTextBox.Text.Trim();

    public InputDialog()
    {
        InitializeComponent();
        UrlTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
        {
            UrlTextBox.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(UrlTextBox.Text))
        {
            DialogResult = true;
            Close();
        }
    }
}

