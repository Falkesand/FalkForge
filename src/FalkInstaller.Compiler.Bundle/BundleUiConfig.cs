namespace FalkInstaller.Compiler.Bundle;

public sealed class BundleUiConfig
{
    public required BundleUiType UiType { get; init; }
    public string? LicenseFile { get; init; }
    public string? LogoFile { get; init; }
    public string? ThemeColor { get; init; }
}
