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
            if (DataContext is DatabaseServerPage page)
                await page.SearchServersAsync();
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