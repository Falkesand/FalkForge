using System.Windows;
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

    /// <summary>
    /// Factory delegate that creates the custom installer window.
    /// Replaces the former <c>CustomWindowType</c> property to avoid
    /// <see cref="System.Activator.CreateInstance"/> and support NativeAOT trimming.
    /// </summary>
    public Func<Window>? CustomWindowFactory { get; init; }

    /// <summary>
    /// Kept for source-compatibility. Prefer <see cref="CustomWindowFactory"/> for
    /// NativeAOT / trimming safety. When both are set, <see cref="CustomWindowFactory"/>
    /// takes precedence.
    /// </summary>
    public Type? CustomWindowType { get; init; }

    public string? WatermarkImagePath { get; init; }
    public string? BannerImagePath { get; init; }
    public string? BannerIconPath { get; init; }
}