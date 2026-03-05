namespace FalkForge.Ui.Abstractions;

using FalkForge.Engine.Protocol;

/// <summary>
/// Extension methods for <see cref="FeatureState"/> detection results.
/// </summary>
public static class FeatureStateExtensions
{
    /// <summary>
    /// Determines the install status of a feature given the overall bundle state.
    /// </summary>
    public static FeatureInstallStatus GetInstallStatus(this FeatureState feature, InstallState bundleState)
    {
        if (!feature.WasPreviouslyInstalled)
            return FeatureInstallStatus.New;

        return bundleState switch
        {
            InstallState.OlderVersion => FeatureInstallStatus.UpgradeAvailable,
            InstallState.NewerVersion => FeatureInstallStatus.Downgrade,
            InstallState.Installed => FeatureInstallStatus.Installed,
            _ => FeatureInstallStatus.New
        };
    }
}
