namespace FalkForge.Compiler.Bundle;

public sealed class BundleUiConfig
{
    public required BundleUiType UiType { get; init; }
    public string? LicenseFile { get; init; }
    public string? LogoFile { get; init; }
    public string? ThemeColor { get; init; }
    public string? CustomUiProjectPath { get; init; }
    public string? WatermarkImage { get; init; }
    public string? BannerImage { get; init; }
    public string? BannerIcon { get; init; }
}
