namespace FalkForge.Decompiler;

/// <summary>
/// Tracks a WiX Burn feature that has no direct mapping to the FalkForge model.
/// Categories: Variable, Search, BootstrapperApplication, MsiProperty,
/// ExitCode, ApprovedExeForElevation, BootstrapperExtension.
/// </summary>
public sealed record WixUnmappedFeature(string Category, string Description, string OriginalXml);
