namespace FalkForge.Platform.Dependencies;

using System.Text.RegularExpressions;
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
public static partial class DependencyRegistrationPaths
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

    /// <summary>
    /// Traversal/injection guard for a single registry key SEGMENT (never a whole path — a segment is
    /// always interpolated directly into one of the fixed templates above, e.g.
    /// <c>Dependencies\{key}\Dependents\{key}</c>). Allows the common WiX/Burn GUID-style provider key
    /// (e.g. <c>{12345678-...}</c>) in addition to plain alphanumeric identifiers; rejects backslash and
    /// forward slash so a crafted key cannot expand into unexpected subkey structure, rejects empty and
    /// whitespace-only segments, and rejects a trailing newline (a bare <c>$</c> anchor in .NET matches
    /// before a trailing <c>\n</c> — do NOT reintroduce <c>^</c>/<c>$</c> here; always use <c>\A</c>/<c>\z</c>).
    ///
    /// <para>
    /// This is the SINGLE validator for every writer of the <c>Dependencies\</c> layout — both
    /// <see cref="DependencyRegistrar"/> (used directly by the unprivileged <c>PerUser</c> write path
    /// and, wrapped in a <c>HKLM</c>-backed <see cref="FalkForge.IRegistry"/>, by the elevated
    /// <c>DependencyRegistrationCommand</c>) call this same method rather than keeping their own copies,
    /// so a manifest-sourced (attacker-authorable) provider/consumer key is checked identically no
    /// matter which path writes it.
    /// </para>
    /// </summary>
    public static bool IsSafeKeySegment(string segment) =>
        segment.Length is > 0 and <= 255 && SafeKeySegmentPattern().IsMatch(segment);

    [GeneratedRegex(@"\A[A-Za-z0-9{][A-Za-z0-9 ._\-{}]*\z")]
    private static partial Regex SafeKeySegmentPattern();
}
