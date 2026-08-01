namespace FalkForge.Platform.Dependencies;

using FalkForge;

/// <summary>
/// Write side of runtime dependency enforcement. The ONLY writer of the
/// <see cref="DependencyRegistrationPaths"/> layout at bundle-install time (a separate, compile-time
/// mechanism — <c>FalkForge.Extensions.Dependency.DependencyTableContributor</c> — emits the same layout
/// as MSI Registry-table rows for Windows Installer's own writer). Used directly (non-elevated) for
/// <see cref="InstallScope.PerUser"/> writes to <see cref="RegistryRoot.CurrentUser"/>, and by the
/// elevated companion's <c>DependencyRegistrationCommand</c> (wrapping a <see cref="RegistryRoot.LocalMachine"/>
/// <see cref="FalkForge.Platform.IRegistry"/>) for <see cref="InstallScope.PerMachine"/> writes.
/// </summary>
public sealed class DependencyRegistrar
{
    private readonly IRegistry _registry;

    public DependencyRegistrar(IRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Registers (or refreshes) a provider's <c>Version</c> and optional <c>DisplayName</c>. Idempotent —
    /// installing the same provider again just overwrites its values.
    /// </summary>
    public void RegisterProvider(RegistryRoot root, string providerKey, string version, string? displayName)
    {
        var path = DependencyRegistrationPaths.ProviderKeyPath(providerKey);
        _registry.SetStringValue(root, path, "Version", version);
        if (displayName is not null)
            _registry.SetStringValue(root, path, "DisplayName", displayName);
    }

    /// <summary>
    /// Registers a consumer's dependency on <paramref name="providerKey"/> by creating its Dependents
    /// subkey (<see cref="IRegistry"/> has no explicit CreateKey — writing a value into the path creates
    /// it) and stamping <paramref name="bundleId"/> inside it, so a future orphan-collection pass can tell
    /// which bundle owns the registration.
    /// </summary>
    public void RegisterConsumer(RegistryRoot root, string providerKey, string consumerKey, Guid bundleId)
    {
        var path = DependencyRegistrationPaths.ConsumerKeyPath(providerKey, consumerKey);
        _registry.SetStringValue(root, path, "BundleId", bundleId.ToString());
    }

    /// <summary>
    /// Removes ONLY this consumer's own registration subkey. Never touches the provider's key, the
    /// Dependents key itself, or any other consumer's subkey — this is what makes the reference count
    /// work: the provider (and its Dependents key, if other consumers remain) survives as long as any
    /// consumer is still registered.
    /// </summary>
    public void UnregisterConsumer(RegistryRoot root, string providerKey, string consumerKey)
    {
        var path = DependencyRegistrationPaths.ConsumerKeyPath(providerKey, consumerKey);
        _registry.DeleteKey(root, path);
    }
}
