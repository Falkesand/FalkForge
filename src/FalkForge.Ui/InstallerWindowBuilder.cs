namespace FalkForge.Ui;

using System.Windows;
using System.Windows.Media;

public sealed class InstallerWindowBuilder
{
    private double _width = 600;
    private double _height = 400;
    private bool _isBorderless;
    private double _cornerRadius;
    private Color? _backgroundColor;
    private Color? _accentColor;
    private string? _title;
    private string? _iconPath;
    private Type? _customWindowType;

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

    internal InstallerWindowConfig Build() => new()
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
    };
}
