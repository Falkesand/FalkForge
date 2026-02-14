namespace FalkInstaller.Ui.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var invert = parameter is "Invert";
            return (boolValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            var invert = parameter is "Invert";
            return (visibility == Visibility.Visible) ^ invert;
        }

        return false;
    }
}
