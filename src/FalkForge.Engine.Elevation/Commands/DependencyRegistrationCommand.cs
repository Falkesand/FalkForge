namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.Versioning;
using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Platform;
using FalkForge.Platform.Dependencies;
using FalkForge.Platform.Windows;

/// <summary>
/// Whitelisted elevated command for runtime dependency enforcement's per-machine write side. A SEPARATE
/// command from <see cref="RegistryWriteCommand"/> — that command's allowlist permanently reserves
/// <c>SOFTWARE\Classes\...</c> (COM/shell hijack surface), so un-reserving it for this one path would
/// reopen that surface for every other caller. This command's own allowlist is scoped to exactly
/// <c>SOFTWARE\Classes\Installer\Dependencies\</c> via <see cref="DependencyRegistrationPaths"/>.
///
/// <para>
/// Every provider/consumer key SEGMENT — sourced from the manifest, which can be attacker-authored — is
/// traversal-checked via <see cref="DependencyRegistrationPaths.IsSafeKeySegment"/> before it is
/// interpolated into a registry path. That guard is shared, feature-wide, single-source-of-truth
/// validation: <see cref="DependencyRegistrar"/> (the only writer of the registry layout, used directly
/// by this command AND by the unprivileged <c>PerUser</c> write path in <c>ApplyStep</c>) enforces it
/// internally, so every caller is covered, not just this elevated command. The loop below is an
/// additional up-front, all-or-nothing pre-check specific to this command: it rejects the ENTIRE payload
/// before writing anything if any single key is unsafe, rather than partially applying a batch.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DependencyRegistrationCommand : IElevatedCommand
{
    private readonly IRegistry _registry;

    /// <summary>Production ctor: writes through the real Windows registry.</summary>
    public DependencyRegistrationCommand()
        : this(new WindowsRegistry())
    {
    }

    /// <summary>Test ctor: writes through the supplied <see cref="IRegistry"/> (e.g. a mock).</summary>
    internal DependencyRegistrationCommand(IRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public string Name => "DependencyRegistration";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!DependencyRegistrationPayload.TryDeserialize(
                payload, out var opcode, out var bundleId, out var providers, out var consumers))
        {
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                "DependencyRegistration: malformed payload; refusing to touch the registry.");
        }

        foreach (var provider in providers)
        {
            if (!DependencyRegistrationPaths.IsSafeKeySegment(provider.Key))
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    $"DependencyRegistration: unsafe provider key segment '{provider.Key}'.");
        }

        foreach (var consumer in consumers)
        {
            if (!DependencyRegistrationPaths.IsSafeKeySegment(consumer.ProviderKey) ||
                !DependencyRegistrationPaths.IsSafeKeySegment(consumer.ConsumerKey))
            {
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    $"DependencyRegistration: unsafe dependency key segment " +
                    $"('{consumer.ProviderKey}'/'{consumer.ConsumerKey}').");
            }
        }

        var registrar = new DependencyRegistrar(_registry);

        try
        {
            if (opcode == DependencyRegistrationOpcode.Register)
            {
                foreach (var provider in providers)
                {
                    var providerResult = registrar.RegisterProvider(
                        RegistryRoot.LocalMachine, provider.Key, provider.Version, provider.DisplayName);
                    if (providerResult.IsFailure)
                        return Result<byte[]>.Failure(providerResult.Error);
                }

                foreach (var consumer in consumers)
                {
                    var consumerResult = registrar.RegisterConsumer(
                        RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey, bundleId);
                    if (consumerResult.IsFailure)
                        return Result<byte[]>.Failure(consumerResult.Error);
                }
            }
            else
            {
                // Ownership check per consumer, tolerant rather than all-or-nothing: a row owned by a
                // DIFFERENT bundle than the one requesting the unregister is SKIPPED, not treated as a
                // reason to fail the whole batch. RegisterConsumer already stamps a BundleId value into
                // each consumer subkey for exactly this purpose. Skipping the row still enforces the
                // ownership invariant (a foreign-owned row is never deleted), and it deletes strictly
                // FEWER keys than an untolerant pass would — this bundle's own rows are still removed even
                // when one shared row was overwritten with a different bundle's id, so a caller can no
                // longer wedge the whole unregister by poisoning a single row it does not own. A missing
                // BundleId value (no prior owner recorded — e.g. an MSI-authored entry predating this
                // feature) is not treated as a mismatch, so unregistering it is still allowed.
                foreach (var consumer in consumers)
                {
                    var consumerPath = DependencyRegistrationPaths.ConsumerKeyPath(
                        consumer.ProviderKey, consumer.ConsumerKey);
                    var existingOwner = _registry.GetStringValue(RegistryRoot.LocalMachine, consumerPath, "BundleId");
                    if (existingOwner is not null &&
                        !string.Equals(existingOwner, bundleId.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var unregisterResult = registrar.UnregisterConsumer(
                        RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey);
                    if (unregisterResult.IsFailure)
                        return Result<byte[]>.Failure(unregisterResult.Error);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<byte[]>.Failure(ErrorKind.ElevationError, $"Access denied: {ex.Message}");
        }

        return Array.Empty<byte>();
    }
}
