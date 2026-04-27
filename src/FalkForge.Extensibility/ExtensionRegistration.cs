namespace FalkForge.Extensibility;

/// <summary>
/// Centralised registration helper that enforces extension identity uniqueness and host
/// version compatibility before invoking <see cref="IFalkForgeExtension.Register"/>.
/// Consumers (compilers, hosts, tests) are encouraged to route extension registration
/// through this helper so that duplicate names and incompatible plugins surface as
/// <see cref="PluginCompatibilityException"/> at registration time rather than
/// silently shadowing or producing late, hard-to-diagnose failures.
/// </summary>
public static class ExtensionRegistration
{
    /// <summary>
    /// The semantic version of the Extensibility contract this build of FalkForge ships
    /// with. Bump when contract-breaking changes are introduced; extensions can opt in to
    /// the new contract by setting <see cref="IFalkForgeExtension.MinHostVersion"/>.
    /// </summary>
    public const string CurrentHostVersion = "1.0.0";

    /// <summary>
    /// Registers <paramref name="extension"/> with <paramref name="registry"/> after
    /// verifying that:
    /// <list type="bullet">
    /// <item><description><see cref="IFalkForgeExtension.Name"/> is non-empty.</description></item>
    /// <item><description>No previously registered extension shares the same Name.</description></item>
    /// <item><description><see cref="IFalkForgeExtension.MinHostVersion"/> (when set) is satisfied
    /// by <paramref name="hostVersion"/>.</description></item>
    /// </list>
    /// On success, the extension's Name is added to <paramref name="registeredNames"/>.
    /// On failure a <see cref="PluginCompatibilityException"/> is thrown and the registry
    /// is left untouched.
    /// </summary>
    public static void Register(
        IFalkForgeExtension extension,
        IExtensionRegistry registry,
        ICollection<string> registeredNames,
        string hostVersion = CurrentHostVersion)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(registeredNames);
        ArgumentNullException.ThrowIfNull(hostVersion);

        if (string.IsNullOrWhiteSpace(extension.Name))
        {
            throw new PluginCompatibilityException(
                $"Extension of type '{extension.GetType().FullName}' has an empty Name. Plugin Name is required for conflict detection.");
        }

        if (registeredNames.Contains(extension.Name))
        {
            throw new PluginCompatibilityException(
                $"Extension '{extension.Name}' (version {extension.Version}) cannot be registered: another extension with the same Name has already been registered.");
        }

        if (extension.MinHostVersion is { } minHost && CompareSemVer(minHost, hostVersion) > 0)
        {
            throw new PluginCompatibilityException(
                $"Extension '{extension.Name}' requires host version >= {minHost} but current host is {hostVersion}. Upgrade FalkForge or use an older extension build.");
        }

        extension.Register(registry);
        registeredNames.Add(extension.Name);
    }

    /// <summary>
    /// Compares two dotted-numeric SemVer strings (e.g. "1.0.0", "2.3"). Pre-release
    /// suffixes are not interpreted; only the numeric major.minor.patch prefix is compared.
    /// Missing components are treated as zero (so "1" == "1.0.0").
    /// </summary>
    /// <returns>Negative if <paramref name="left"/> &lt; <paramref name="right"/>, zero if equal, positive if greater.</returns>
    public static int CompareSemVer(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftParts = StripSuffix(left).Split('.');
        var rightParts = StripSuffix(right).Split('.');
        var max = Math.Max(leftParts.Length, rightParts.Length);

        for (var i = 0; i < max; i++)
        {
            var l = i < leftParts.Length && int.TryParse(leftParts[i], out var lv) ? lv : 0;
            var r = i < rightParts.Length && int.TryParse(rightParts[i], out var rv) ? rv : 0;
            if (l != r) return l.CompareTo(r);
        }

        return 0;
    }

    private static string StripSuffix(string version)
    {
        var dashIndex = version.IndexOf('-', StringComparison.Ordinal);
        return dashIndex >= 0 ? version[..dashIndex] : version;
    }
}
