namespace FalkForge.Platform.Dependencies;

using FalkForge;

/// <summary>
/// Registry path layout for runtime dependency enforcement (uninstall must refuse when other packages
/// still depend on a shared component; install must refuse when a required provider is missing).
///
/// <para>
/// The paths here MUST stay byte-identical to <c>FalkForge.Extensions.Dependency
/// .DependencyTableContributor</c> (provider row at line 45, consumer row at line 77 there) — that
/// contributor emits the same layout as Registry-table rows for Windows Installer to write at MSI
/// install time (the WiX/Burn-compatible convention). This class is the runtime writer/reader
/// counterpart used by the bundle engine (<see cref="DependencyRegistrar"/> writes,
/// <c>FalkForge.Engine.Detection.DependencyDetector</c> reads) so an MSI-authored dependency and a
/// bundle-enforced one are visible to each other under the same registry subtree.
/// </para>
/// </summary>
public static class DependencyRegistrationPaths
{
    private const string Base = @"SOFTWARE\Classes\Installer\Dependencies\";

    /// <summary>Registry path holding a provider's <c>Version</c>/<c>DisplayName</c> values.</summary>
    public static string ProviderKeyPath(string providerKey) => $@"{Base}{providerKey}";

    /// <summary>Registry path under which each dependant of <paramref name="providerKey"/> registers a subkey.</summary>
    public static string DependentsKeyPath(string providerKey) => $@"{Base}{providerKey}\Dependents";

    /// <summary>Registry path of a single consumer's registration subkey under its provider's Dependents key.</summary>
    public static string ConsumerKeyPath(string providerKey, string consumerKey) =>
        $@"{Base}{providerKey}\Dependents\{consumerKey}";

    /// <summary>
    /// The single registry root a package of the given <paramref name="scope"/> writes its own
    /// provider/consumer registrations into — <see cref="InstallScope.PerUser"/> writes
    /// <see cref="RegistryRoot.CurrentUser"/> (no elevation needed); <see cref="InstallScope.PerMachine"/>
    /// writes <see cref="RegistryRoot.LocalMachine"/> (through the elevated companion).
    /// </summary>
    public static RegistryRoot WriteRootForScope(InstallScope scope) => scope switch
    {
        InstallScope.PerUser => RegistryRoot.CurrentUser,
        InstallScope.PerMachine => RegistryRoot.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };

    /// <summary>
    /// Both roots detection must union-read: a per-user consumer of a per-machine provider (or vice
    /// versa) is a real shape, so a dependency check that only looked at the writer's own scope would
    /// miss it.
    /// </summary>
    public static IReadOnlyList<RegistryRoot> ReadRoots { get; } =
        [RegistryRoot.LocalMachine, RegistryRoot.CurrentUser];
}
