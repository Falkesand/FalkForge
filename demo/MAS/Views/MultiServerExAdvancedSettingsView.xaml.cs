using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MAS.Pages;
using MAS.Shell;

namespace MAS.Views;

public partial class MultiServerExAdvancedSettingsView : UserControl
{
    private Storyboard? _bounceStoryboard;

    public MultiServerExAdvancedSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MultiServerExAdvancedSettingsPage page)
            page.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MultiServerExAdvancedSettingsPage.IsChecking))
                {
                    if (page.IsChecking) StartBounce();
                    else StopBounce();
                }
            };
    }

    private void StartBounce()
    {
        Dispatcher.Invoke(() =>
        {
            _bounceStoryboard = BounceAnimationHelper.CreateBounce(CheckBounceBar, CheckButton.ActualWidth - 2);
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