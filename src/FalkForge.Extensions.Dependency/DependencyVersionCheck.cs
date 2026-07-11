namespace FalkForge.Extensions.Dependency;

/// <summary>
///     One planned MSI-time dependency version check: the synthetic AppSearch/RegLocator
///     property and signature names, the provider's registry key path, the LaunchCondition
///     that must hold true for install to proceed, and the human-readable abort message.
///     Produced by <see cref="DependencyVersionCheckPlanner"/> and consumed by the
///     RegLocator/AppSearch/LaunchCondition table contributors.
/// </summary>
internal sealed record DependencyVersionCheck(
    string PropertyName,
    string SignatureName,
    string RegistryKeyPath,
    string Condition,
    string Message);
