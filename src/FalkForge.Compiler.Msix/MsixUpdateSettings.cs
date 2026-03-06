namespace FalkForge.Compiler.Msix;

public sealed class MsixUpdateSettings
{
    public required string AppInstallerUri { get; init; }
    public int HoursBetweenUpdateChecks { get; init; } = 24;
    public bool ShowPrompt { get; init; }
    public bool UpdateBlocksActivation { get; init; }
    public bool AutomaticBackgroundTask { get; init; }
    public bool ForceUpdateFromAnyVersion { get; init; }
}
