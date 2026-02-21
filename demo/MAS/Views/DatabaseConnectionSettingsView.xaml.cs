using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class DatabaseConnectionSettingsView : UserControl
{
    public DatabaseConnectionSettingsView() => InitializeComponent();

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is DatabaseConnectionSettingsPage page)
                await page.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Test connection failed: {ex.Message}");
        }
    }
}
