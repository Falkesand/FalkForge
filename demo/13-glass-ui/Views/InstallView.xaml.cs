using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GlassUi.Pages;

namespace GlassUi.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        InitializeComponent();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is InstallPage page)
                await page.InstallAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Install failed: {ex.Message}");
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}