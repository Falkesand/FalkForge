namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Describes the install status of a single feature relative to the detected bundle state.
/// </summary>
public enum FeatureInstallStatus
{
    /// <summary>Feature was not previously installed.</summary>
    New,

    /// <summary>Feature is installed at the current version.</summary>
    Installed,

    /// <summary>An older version of the feature is installed; an upgrade is available.</summary>
    UpgradeAvailable,

    /// <summary>A newer version of the feature is installed; installing would downgrade.</summary>
    Downgrade
}
