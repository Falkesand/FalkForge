using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class DatabaseServerView : UserControl
{
    public DatabaseServerView()
    {
        InitializeComponent();
    }

    private async void SearchServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not DatabaseServerPage page) return;

            var typedServer = page.DatabaseServer;
            await page.SearchServersAsync();

            // If the typed server wasn't found in the results, open the dropdown
            // so the user can pick from what was discovered.
            // If it WAS found, do nothing — the current value is valid.
            var found = page.AvailableServers.Any(s =>
                string.Equals(s, typedServer, StringComparison.OrdinalIgnoreCase));

            if (!found && page.AvailableServers.Count > 0)
                ServerCombo.IsDropDownOpen = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Search failed: {ex.Message}");
        }
    }

    private async void DatabaseCombo_DropDownOpened(object sender, EventArgs e)
    {
        try
        {
            if (DataContext is DatabaseServerPage page)
                await page.LoadDatabasesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Load databases failed: {ex.Message}");
        }
    }
}