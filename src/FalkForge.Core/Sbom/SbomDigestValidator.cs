namespace FalkForge.Sbom;

/// <summary>
/// Validates that a string is a well-formed SHA-256 hex digest before it is serialized into an
/// SBOM sidecar as an integrity claim: exactly 64 hexadecimal characters. Both cases are
/// accepted and neither is normalized — the sidecar stores whatever case the caller supplied.
/// </summary>
/// <remarks>
/// Shared by the MSI, Bundle, and MSIX SBOM writers' <c>AdditionalComponents</c> validation, and
/// by <c>BundleValidator</c>'s remote-payload public-key pin check (BDL033), which is shaped
/// identically. All four call sites previously carried their own copy of this exact loop; this
/// type is the single source of truth so they cannot drift. Avoids regex/LINQ to keep the
/// validation path allocation-free (Gate 6).
/// </remarks>
public static class SbomDigestValidator
{
    private const int Sha256HexLength = 64;
    private const int Sha1HexLength = 40;

    public static bool IsValidSha256Hex(string value) => IsHexOfLength(value, Sha256HexLength);

    /// <summary>
    /// Validates a SHA-1 hex digest: exactly 40 hexadecimal characters, either case, unnormalized.
    /// Used by <see cref="SpdxSbomGenerator"/>, where SPDX 2.3 §8.4 makes a per-file SHA1 checksum
    /// mandatory. SHA-1 appears in FalkForge solely as that spec-mandated identifier; nothing makes
    /// a trust decision on it, and this method asserts shape only, never fitness for one.
    /// </summary>
    public static bool IsValidSha1Hex(string value) => IsHexOfLength(value, Sha1HexLength);

    private static bool IsHexOfLength(string value, int expectedLength)
    {
        if (value.Length != expectedLength)
            return false;

        foreach (var c in value)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }
}
