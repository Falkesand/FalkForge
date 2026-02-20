using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class DatabaseConnectionSettingsView : UserControl
{
    public DatabaseConnectionSettingsView() => InitializeComponent();

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is DatabaseConnectionSettingsPage page)
            page.Password = ((PasswordBox)sender).Password;
    }
}
