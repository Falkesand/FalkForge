using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MAS.Pages;
using MAS.Shell;

namespace MAS.Views;

public partial class DatabaseServerView : UserControl
{
    private Storyboard? _bounceStoryboard;

    public DatabaseServerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is DatabaseServerPage page)
            page.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DatabaseServerPage.IsSearching))
                {
                    if (page.IsSearching) StartBounce();
                    else StopBounce();
                }
            };
    }

    private void StartBounce()
    {
        Dispatcher.Invoke(() =>
        {
            _bounceStoryboard = BounceAnimationHelper.CreateBounce(BounceBar, SearchButton.ActualWidth - 2);
            _bounceStoryboard.Begin();
        });
    }

    private void StopBounce()
    {
        Dispatcher.Invoke(() =>
        {
            _bounceStoryboard?.Stop();
            _bounceStoryboard = null;
        });
    }

    private async void SearchServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not DatabaseServerPage page) return;

            var typedServer = page.DatabaseServer;
            await page.SearchServersAsync();

            // .\SQLEXPRESS is shorthand for MACHINENAME\SQLEXPRESS
            var normalized = typedServer.Replace(@".\", Environment.MachineName + @"\", StringComparison.OrdinalIgnoreCase);
            var found = page.AvailableServers.Any(s =>
                string.Equals(s, typedServer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase));

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