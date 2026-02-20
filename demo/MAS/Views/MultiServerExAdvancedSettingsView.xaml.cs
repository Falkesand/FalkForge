using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class MultiServerExAdvancedSettingsView : UserControl
{
    public MultiServerExAdvancedSettingsView() => InitializeComponent();

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerExAdvancedSettingsPage page)
            page.ServicePassword = ((PasswordBox)sender).Password;
    }
}
