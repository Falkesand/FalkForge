namespace FalkForge.Ui.Converters;

using System.Globalization;
using System.Windows.Data;

[ValueConversion(typeof(double), typeof(double))]
public sealed class ProgressToWidthConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is double progress
            && values[1] is double containerWidth)
        {
            var clamped = Math.Clamp(progress, 0.0, 100.0);
            return clamped / 100.0 * containerWidth;
        }

        return 0.0;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return [Binding.DoNothing, Binding.DoNothing];
    }
}
