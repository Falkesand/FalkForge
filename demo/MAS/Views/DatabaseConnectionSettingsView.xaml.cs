using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MAS.Pages;
using MAS.Shell;

namespace MAS.Views;

public partial class DatabaseConnectionSettingsView : UserControl
{
    private Storyboard? _bounceStoryboard;

    public DatabaseConnectionSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is DatabaseConnectionSettingsPage page)
            page.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DatabaseConnectionSettingsPage.IsTesting))
                {
                    if (page.IsTesting) StartBounce();
                    else StopBounce();
                }
            };
    }

    private void StartBounce()
    {
        Dispatcher.Invoke(() =>
        {
            _bounceStoryboard = BounceAnimationHelper.CreateBounce(TestBounceBar, TestButton.ActualWidth - 2);
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

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is DatabaseConnectionSettingsPage page)
                await page.TestConnectionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Test connection failed: {ex.Message}");
        }
    }
}