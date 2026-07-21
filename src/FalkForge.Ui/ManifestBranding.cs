using System.Windows.Media;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Ui;

/// <summary>
/// Merges bundle-authored branding carried on the <see cref="InstallerManifest"/> — logo, theme
/// color, watermark, banner and banner icon, all set via <c>BundleBuilder.UseBuiltInUI(...)</c> —
/// into an <see cref="InstallerWindowConfig"/>. A manifest value fills a config field only when the
/// caller left that field unset, so an explicit <c>InstallerUIBuilder.Window(...)</c> setting always
/// wins over the bundle-authored default.
/// </summary>
internal static class ManifestBranding
{
    public static InstallerWindowConfig Merge(InstallerWindowConfig config, InstallerManifest manifest)
    {
        return new InstallerWindowConfig
        {
            Width = config.Width,
            Height = config.Height,
            IsBorderless = config.IsBorderless,
            CornerRadius = config.CornerRadius,
            BackgroundColor = config.BackgroundColor,
            AccentColor = config.AccentColor ?? TryParseColor(manifest.ThemeColor),
            Title = config.Title,
            IconPath = config.IconPath ?? NullIfWhitespace(manifest.LogoFile),
            CustomWindowFactory = config.CustomWindowFactory,
            CustomWindowType = config.CustomWindowType,
            WatermarkImagePath = config.WatermarkImagePath ?? NullIfWhitespace(manifest.WatermarkImage),
            BannerImagePath = config.BannerImagePath ?? NullIfWhitespace(manifest.BannerImage),
            BannerIconPath = config.BannerIconPath ?? NullIfWhitespace(manifest.BannerIcon)
        };
    }

    private static Color? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or NotSupportedException)
        {
            // Best-effort: an unparseable theme color is ignored rather than fatal.
            // ColorConverter.ConvertFromString throws FormatException for malformed hex/name
            // strings, but its TypeConverter plumbing can also surface InvalidOperationException
            // (culture-info conversion failure) or NotSupportedException (unconvertible value) --
            // catch the realistic set instead of just FormatException so a bad author-supplied
            // theme color never crashes UI startup.
            return null;
        }
    }

    private static string? NullIfWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
