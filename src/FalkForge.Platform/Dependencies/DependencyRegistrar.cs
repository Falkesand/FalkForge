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
    /// installing the same provider again just overwrites its values. Every key segment is validated by
    /// <see cref="DependencyRegistrationPaths.IsSafeKeySegment"/> BEFORE anything is interpolated into a
    /// registry path — this method is the single writer both the unprivileged <c>PerUser</c> path and the
    /// elevated <c>HKLM</c> companion go through, so this guard covers manifest-sourced (attacker-
    /// authorable) keys regardless of which caller supplies them.
    /// </summary>
    public Result<Unit> RegisterProvider(RegistryRoot root, string providerKey, string version, string? displayName)
    {
        if (!DependencyRegistrationPaths.IsSafeKeySegment(providerKey))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"DependencyRegistrar: unsafe provider key segment '{providerKey}'.");

        var path = DependencyRegistrationPaths.ProviderKeyPath(providerKey);
        _registry.SetStringValue(root, path, "Version", version);
        if (displayName is not null)
            _registry.SetStringValue(root, path, "DisplayName", displayName);

        return Unit.Value;
    }

    /// <summary>
    /// Registers a consumer's dependency on <paramref name="providerKey"/> by creating its Dependents
    /// subkey (<see cref="IRegistry"/> has no explicit CreateKey — writing a value into the path creates
    /// it) and stamping <paramref name="bundleId"/> inside it, so a future orphan-collection pass can tell
    /// which bundle owns the registration. Both key segments are validated before anything is written —
    /// see <see cref="RegisterProvider"/> remarks.
    /// </summary>
    public Result<Unit> RegisterConsumer(RegistryRoot root, string providerKey, string consumerKey, Guid bundleId)
    {
        if (!DependencyRegistrationPaths.IsSafeKeySegment(providerKey) ||
            !DependencyRegistrationPaths.IsSafeKeySegment(consumerKey))
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"DependencyRegistrar: unsafe dependency key segment ('{providerKey}'/'{consumerKey}').");
        }

        var path = DependencyRegistrationPaths.ConsumerKeyPath(providerKey, consumerKey);
        _registry.SetStringValue(root, path, "BundleId", bundleId.ToString());

        return Unit.Value;
    }

    /// <summary>
    /// Removes ONLY this consumer's own registration subkey. Never touches the provider's key, the
    /// Dependents key itself, or any other consumer's subkey — this is what makes the reference count
    /// work: the provider (and its Dependents key, if other consumers remain) survives as long as any
    /// consumer is still registered. Both key segments are validated before the delete is attempted — an
    /// unvalidated empty/backslash-bearing consumer key here is what let a crafted manifest reach
    /// <see cref="IRegistry.DeleteKey"/> with an attacker-controlled path (see ADR 0008 amendment); see
    /// <see cref="RegisterProvider"/> remarks for why the guard lives here rather than only in the
    /// elevated command.
    /// </summary>
    public Result<Unit> UnregisterConsumer(RegistryRoot root, string providerKey, string consumerKey)
    {
        if (!DependencyRegistrationPaths.IsSafeKeySegment(providerKey) ||
            !DependencyRegistrationPaths.IsSafeKeySegment(consumerKey))
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"DependencyRegistrar: unsafe dependency key segment ('{providerKey}'/'{consumerKey}').");
        }

        var path = DependencyRegistrationPaths.ConsumerKeyPath(providerKey, consumerKey);
        _registry.DeleteKey(root, path);

        return Unit.Value;
    }
}
