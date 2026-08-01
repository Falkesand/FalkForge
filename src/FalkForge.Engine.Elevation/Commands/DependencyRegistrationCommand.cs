namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Platform;
using FalkForge.Platform.Dependencies;
using FalkForge.Platform.Windows;

/// <summary>
/// Whitelisted elevated command for runtime dependency enforcement's per-machine write side. A SEPARATE
/// command from <see cref="RegistryWriteCommand"/> — that command's allowlist permanently reserves
/// <c>SOFTWARE\Classes\...</c> (COM/shell hijack surface), so un-reserving it for this one path would
/// reopen that surface for every other caller. This command's own allowlist is scoped to exactly
/// <c>SOFTWARE\Classes\Installer\Dependencies\</c> via <see cref="DependencyRegistrationPaths"/>, and every
/// provider/consumer key SEGMENT — sourced from the manifest, which can be attacker-authored — is
/// traversal-checked before it is interpolated into a registry path.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class DependencyRegistrationCommand : IElevatedCommand
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
            if (!IsSafeKeySegment(provider.Key))
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    $"DependencyRegistration: unsafe provider key segment '{provider.Key}'.");
        }

        foreach (var consumer in consumers)
        {
            if (!IsSafeKeySegment(consumer.ProviderKey) || !IsSafeKeySegment(consumer.ConsumerKey))
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    $"DependencyRegistration: unsafe dependency key segment " +
                    $"('{consumer.ProviderKey}'/'{consumer.ConsumerKey}').");
        }

        var registrar = new DependencyRegistrar(_registry);

        try
        {
            if (opcode == DependencyRegistrationOpcode.Register)
            {
                foreach (var provider in providers)
                    registrar.RegisterProvider(RegistryRoot.LocalMachine, provider.Key, provider.Version, provider.DisplayName);

                foreach (var consumer in consumers)
                    registrar.RegisterConsumer(RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey, bundleId);
            }
            else
            {
                foreach (var consumer in consumers)
                    registrar.UnregisterConsumer(RegistryRoot.LocalMachine, consumer.ProviderKey, consumer.ConsumerKey);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<byte[]>.Failure(ErrorKind.ElevationError, $"Access denied: {ex.Message}");
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// Traversal/injection guard for a single registry key SEGMENT (never a whole path — the segment is
    /// interpolated directly into the fixed <c>Dependencies\{key}\Dependents\{key}</c> template). Allows
    /// the common WiX/Burn GUID-style provider key (e.g. <c>{12345678-...}</c>) in addition to plain
    /// alphanumeric identifiers; rejects backslash so a crafted key cannot expand into unexpected subkey
    /// structure.
    /// </summary>
    private static bool IsSafeKeySegment(string segment) =>
        segment.Length is > 0 and <= 255 && SafeKeySegmentPattern().IsMatch(segment);

    [GeneratedRegex(@"^[A-Za-z0-9{][A-Za-z0-9 ._\-{}]*$")]
    private static partial Regex SafeKeySegmentPattern();
}
