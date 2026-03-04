using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class MultiServerAdvancedSettingsView : UserControl
{
    public MultiServerAdvancedSettingsView()
    {
        InitializeComponent();
    }

    private void CheckDsn_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerAdvancedSettingsPage page)
            page.CheckDsnName();
    }

    private void OdbcAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MultiServerAdvancedSettingsPage page)
            page.LaunchOdbcAdmin();
    }
}