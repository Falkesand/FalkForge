namespace FalkForge.Ui.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

internal partial class CustomInstallerWindow : Window
{
    public CustomInstallerWindow()
    {
        InitializeComponent();
    }

    internal void ApplyConfig(InstallerWindowConfig config)
    {
        Width = config.Width;
        Height = config.Height;

        if (config.Title is not null)
            Title = config.Title;

        if (config.IsBorderless)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            if (config.CornerRadius > 0)
            {
                var border = new Border
                {
                    CornerRadius = new CornerRadius(config.CornerRadius),
                    ClipToBounds = true
                };
                var content = Content as UIElement;
                Content = null;
                border.Child = content;
                Content = border;
            }
        }

        if (config.BackgroundColor.HasValue)
        {
            var brush = new SolidColorBrush(config.BackgroundColor.Value);
            brush.Freeze();
            Resources["WindowBackground"] = brush;
            Background = brush;
        }

        if (config.AccentColor.HasValue)
        {
            var brush = new SolidColorBrush(config.AccentColor.Value);
            brush.Freeze();
            Resources["AccentBrush"] = brush;
        }

        if (config.IconPath is not null)
        {
            try
            {
                var icon = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri(config.IconPath, UriKind.RelativeOrAbsolute));
                Icon = icon;
            }
            catch
            {
                // Icon loading is best-effort
            }
        }

        ApplyImageResource(config.WatermarkImagePath, "WatermarkImage");
        ApplyImageResource(config.BannerImagePath, "BannerImage");
        ApplyImageResource(config.BannerIconPath, "BannerIconImage");
    }

    private void ApplyImageResource(string? path, string resourceKey)
    {
        if (path is null)
            return;

        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage(
                new Uri(path, UriKind.RelativeOrAbsolute));
            Resources[resourceKey] = image;
        }
        catch
        {
            // Image loading is best-effort
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}
