using System.Windows.Media;

namespace FalkForge.Ui;

internal sealed class InstallerWindowConfig
{
    public double Width { get; init; } = 600;
    public double Height { get; init; } = 400;
    public bool IsBorderless { get; init; }
    public double CornerRadius { get; init; }
    public Color? BackgroundColor { get; init; }
    public Color? AccentColor { get; init; }
    public string? Title { get; init; }
    public string? IconPath { get; init; }
    public Type? CustomWindowType { get; init; }
    public string? WatermarkImagePath { get; init; }
    public string? BannerImagePath { get; init; }
    public string? BannerIconPath { get; init; }
}