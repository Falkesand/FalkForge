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

    /// <summary>
    /// Rejects any component whose digests are not shaped like hashes, before a single one is
    /// serialized. A checksum field is an integrity claim, so emitting an arbitrary caller-supplied
    /// string as one lets an SBOM assert something FalkForge never even looked at — and in the
    /// attestation paths that claim ends up inside a signed DSSE envelope.
    /// </summary>
    /// <param name="components">
    /// The components about to be written. In practice this is
    /// <c>SbomOptions.AdditionalComponents</c>: FalkForge's own payload digests come from the
    /// packaging callbacks and are hex by construction, while these arrive straight from
    /// <c>AddComponent</c>, which guards only against whitespace.
    /// </param>
    /// <param name="context">
    /// Which writer is reporting, e.g. <c>"MSI SBOM attestation"</c>. Named rather than inferred so
    /// a publisher reading a build log can tell the sidecar and the attestation apart.
    /// </param>
    /// <remarks>
    /// Lives here rather than being copy-pasted into each writer: the MSI, Bundle and MSIX sidecar
    /// helpers each carried their own identical loop, and the two attestation paths — the
    /// signed ones — carried none at all.
    ///
    /// <para><b>A null <paramref name="components"/> throws rather than returning a failed
    /// <see cref="Result{T}"/>, deliberately.</b> The <c>Result</c> channel here models one thing:
    /// "a digest in this data is not shaped like a hash", an outcome a build can report and a
    /// publisher can fix. A null list is not data — it is a broken call. No caller can reach it
    /// from input: <c>SbomOptions.AdditionalComponents</c> is a computed property over a private
    /// list and <c>SbomDocument.Components</c> is <c>required</c> and non-nullable, so null arrives
    /// only through <c>null!</c>. Folding that into the same channel would make a programming error
    /// look like a recoverable validation finding. <see cref="ReproducibleSbomIdentity.Resolve"/>
    /// guards the very same list the same way a few lines later in every caller.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="components"/> is null.</exception>
    public static Result<Unit> ValidateComponentDigests(
        IReadOnlyList<SbomComponent> components, string context)
    {
        ArgumentNullException.ThrowIfNull(components);

        foreach (var component in components)
        {
            if (!IsValidSha256Hex(component.Sha256Hash))
            {
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"SBM004: {context}: component '{component.Name}' has a digest " +
                    $"'{component.Sha256Hash}' that is not a valid SHA-256 hash (expected 64 " +
                    "hexadecimal characters).");
            }

            // Null is legitimate — SHA-1 is only required for SPDX file components, and
            // SpdxSbomGenerator enforces that itself. A non-null value, though, is a claim.
            if (component.Sha1Hash is { } sha1 && !IsValidSha1Hex(sha1))
            {
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"SBM004: {context}: component '{component.Name}' has a digest '{sha1}' that is " +
                    "not a valid SHA-1 hash (expected 40 hexadecimal characters).");
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

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
