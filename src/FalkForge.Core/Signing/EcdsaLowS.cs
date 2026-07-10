using System.Globalization;
using System.Numerics;

namespace FalkForge.Signing;

/// <summary>
/// Low-S canonicalization for ECDSA-P256 signatures in IEEE P1363 form (r‖s, 64 bytes).
///
/// <para>ECDSA is malleable: for any valid signature (r, s) the twin (r, n − s) — n being the curve
/// group order — is also cryptographically valid over the same message, and .NET's
/// <c>ECDsa.VerifyHash</c> accepts both forms. Canonical "low-S" fixes s to the low half of the group
/// order (s ≤ n/2), so each accepted signature has exactly one byte representation. FalkForge
/// canonicalizes at every sign/normalize site (<see cref="Canonicalize"/>) and rejects non-canonical
/// signatures at verification (<see cref="IsCanonical"/>), so an attacker cannot take a legitimately
/// signed manifest and mint a different-bytes-but-still-valid signature by flipping s → n − s.</para>
///
/// <para>P-256 only, by design: the manifest signature algorithm is pinned to ECDSA-P256
/// (<c>IntegrityEnvelopeCodec.AlgorithmId</c>), whose P1363 form is exactly 64 bytes. Any other
/// length is treated as non-canonical (fail closed), so a future curve addition must extend this type
/// deliberately rather than silently bypassing the check. All values involved (r, s, n) are public,
/// so no constant-time handling is required; <see cref="BigInteger"/> is AOT-safe.</para>
/// </summary>
public static class EcdsaLowS
{
    /// <summary>P-256 P1363 signature length in bytes: 32-byte big-endian r, then 32-byte big-endian s.</summary>
    public const int SignatureLength = 64;

    private const int CoordinateLength = 32;

    // The NIST P-256 (secp256r1) group order n. The leading 0 keeps BigInteger.Parse unsigned.
    private static readonly BigInteger Order = BigInteger.Parse(
        "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // floor(n / 2) — the inclusive upper bound of the canonical (low-S) range.
    private static readonly BigInteger HalfOrder = Order >> 1;

    /// <summary>
    /// Returns true when <paramref name="signature"/> is a 64-byte P-256 P1363 signature whose s lies
    /// in the low half of the group order (s ≤ n/2). Any other length is non-canonical (fail closed —
    /// this system signs with P-256 exclusively).
    /// </summary>
    public static bool IsCanonical(ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureLength)
            return false;

        var s = new BigInteger(signature[CoordinateLength..], isUnsigned: true, isBigEndian: true);
        return s <= HalfOrder;
    }

    /// <summary>
    /// Canonicalizes a 64-byte P-256 P1363 signature to low-S form: when s &gt; n/2, s is replaced in
    /// place by n − s (r unchanged), which is equally valid over the same message. Returns the same
    /// array instance. A signature of any other length is returned unchanged — it is not a P-256
    /// P1363 value, and the verifier rejects it as non-canonical.
    /// </summary>
    public static byte[] Canonicalize(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != SignatureLength)
            return signature;

        var sSpan = signature.AsSpan(CoordinateLength);
        var s = new BigInteger(sSpan, isUnsigned: true, isBigEndian: true);

        // Already canonical — or s ≥ n, which is not a valid ECDSA scalar and can never verify, so
        // there is nothing meaningful to flip.
        if (s <= HalfOrder || s >= Order)
            return signature;

        var flipped = Order - s; // 0 < flipped < n/2 because n/2 < s < n here.
        sSpan.Clear();

        // Right-align the big-endian magnitude into the fixed-width 32-byte s slot.
        var count = flipped.GetByteCount(isUnsigned: true);
        if (!flipped.TryWriteBytes(sSpan[(CoordinateLength - count)..], out _, isUnsigned: true, isBigEndian: true))
            throw new InvalidOperationException("Unreachable: a reduced P-256 scalar always fits its 32-byte slot.");

        return signature;
    }
}
