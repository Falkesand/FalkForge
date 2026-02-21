using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class MultiServerExAdvancedSettingsView : UserControl
{
    public MultiServerExAdvancedSettingsView() => InitializeComponent();

    private void CheckDsn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerExAdvancedSettingsPage page)
            page.CheckDsnName();
    }

    private void OdbcAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerExAdvancedSettingsPage page)
            page.LaunchOdbcAdmin();
    }
}
