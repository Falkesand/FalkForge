using System.Windows;
using System.Windows.Media;

namespace FalkForge.Ui;

public sealed class InstallerWindowBuilder
{
    private Color? _accentColor;
    private Color? _backgroundColor;
    private string? _bannerIconPath;
    private string? _bannerImagePath;
    private double _cornerRadius;
    private Type? _customWindowType;
    private double _height = 400;
    private string? _iconPath;
    private bool _isBorderless;
    private string? _title;
    private string? _watermarkImagePath;
    private double _width = 600;

    public InstallerWindowBuilder Size(double width, double height)
    {
        _width = width;
        _height = height;
        return this;
    }

    public InstallerWindowBuilder Borderless()
    {
        _isBorderless = true;
        return this;
    }

    public InstallerWindowBuilder CornerRadius(double radius)
    {
        _cornerRadius = radius;
        return this;
    }

    public InstallerWindowBuilder Background(string hex)
    {
        _backgroundColor = (Color)ColorConverter.ConvertFromString(hex);
        return this;
    }

    public InstallerWindowBuilder Accent(string hex)
    {
        _accentColor = (Color)ColorConverter.ConvertFromString(hex);
        return this;
    }

    public InstallerWindowBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    public InstallerWindowBuilder Icon(string iconPath)
    {
        _iconPath = iconPath;
        return this;
    }

    public InstallerWindowBuilder CustomWindow<TWindow>() where TWindow : Window, new()
    {
        _customWindowType = typeof(TWindow);
        return this;
    }

    public InstallerWindowBuilder WatermarkImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _watermarkImagePath = path;
        return this;
    }

    public InstallerWindowBuilder BannerImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _bannerImagePath = path;
        return this;
    }

    public InstallerWindowBuilder BannerIcon(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _bannerIconPath = path;
        return this;
    }

    internal InstallerWindowConfig Build()
    {
        return new InstallerWindowConfig
        {
            Width = _width,
            Height = _height,
            IsBorderless = _isBorderless,
            CornerRadius = _cornerRadius,
            BackgroundColor = _backgroundColor,
            AccentColor = _accentColor,
            Title = _title,
            IconPath = _iconPath,
            CustomWindowType = _customWindowType,
            WatermarkImagePath = _watermarkImagePath,
            BannerImagePath = _bannerImagePath,
            BannerIconPath = _bannerIconPath
        };
    }
}