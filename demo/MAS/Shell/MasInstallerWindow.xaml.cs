using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace MAS.Shell;

public partial class MasInstallerWindow : Window
{
    public MasInstallerWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Use localized strings from page properties, falling back to English
        var page = DataContext?.GetType().GetProperty("CurrentPage")?.GetValue(DataContext);
        var message = page?.GetType().GetProperty("CancelConfirmMessage")?.GetValue(page) as string
                      ?? "Are you sure you want to cancel the installation?";
        var caption = page?.GetType().GetProperty("CancelConfirmTitle")?.GetValue(page) as string
                      ?? "Cancel Installation";

        var result = MessageBox.Show(this, message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes && DataContext is { } vm)
        {
            var cancelCommand = vm.GetType().GetProperty("CancelCommand")?.GetValue(vm) as ICommand;
            cancelCommand?.Execute(null);
        }
    }
}

internal sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
