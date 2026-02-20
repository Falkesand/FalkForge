using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MAS.Shell;

public partial class MasInstallerWindow : Window
{
    public MasInstallerWindow()
    {
        Resources.Add("NullToCollapsedConverter", new NullToCollapsedConverter());
        Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        InitializeComponent();
    }
}

internal sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
