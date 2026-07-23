using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Extensibility;

/// <summary>
/// Shared naming helpers for MSI-native search/version planners (the DotNet extension's runtime
/// search planner and the Dependency extension's version-check planner both need them): a stable
/// content-hash suffix that salts synthetic <c>Signature</c>/<c>Property</c> identifiers so two
/// independently-authored searches — even across separate extension instances in one package —
/// never collide, and a normalizer that renders a <see cref="Version"/> as the unambiguous
/// four-part string the MSI file-version / JScript comparison operands expect. Public rather than
/// <c>internal</c> because the DotNet and Dependency extension assemblies are not granted
/// <c>InternalsVisibleTo</c> by this assembly.
/// </summary>
public static class MsiSearchNaming
{
    /// <summary>
    /// Stable 8-hex-char content hash (first 4 bytes of SHA-256, lowercase hex) of
    /// <paramref name="material"/>, used to salt synthetic MSI identifiers.
    /// </summary>
    public static string Suffix(string material)
    {
        ArgumentNullException.ThrowIfNull(material);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return Convert.ToHexStringLower(hash[..4]);
    }

    /// <summary>
    /// Normalizes a <see cref="Version"/> to a full four-part string (missing Build/Revision become
    /// 0) so file-version / JScript comparison operands are unambiguous.
    /// </summary>
    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0)).ToString(4);
    }
}
