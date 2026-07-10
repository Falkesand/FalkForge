using System.Formats.Asn1;

namespace FalkForge.Signing.SignServer;

/// <summary>
/// Normalizes an ECDSA-P256 signature to canonical low-S IEEE P1363 (fixed 64-byte r‖s) — the encoding
/// the FalkForge integrity verifier expects. SignServer's PlainSigner with SHA256withECDSA returns an
/// ASN.1 DER <c>SEQUENCE { INTEGER r, INTEGER s }</c>; this converts it. A byte-sniff guard keeps the
/// boundary honest against a SignServer version whose encoding drifts: an already-64-byte value is
/// accepted as P1363, a <c>0x30</c>-prefixed DER SEQUENCE is parsed and converted, and anything else
/// fails loud. Either way the result is canonicalized to low-S (<see cref="EcdsaLowS"/>): the remote
/// backend's s-half is outside FalkForge's control, and the verifier rejects high-S signatures.
/// </summary>
internal static class EcdsaSignatureFormatConverter
{
    private const int CoordinateLength = 32; // P-256 field element width in bytes
    private const int P1363Length = CoordinateLength * 2;

    /// <summary>
    /// Returns the P1363 (r‖s, 64-byte) form of <paramref name="signature"/>, or an SGN022 failure if the
    /// bytes are neither a 64-byte P1363 signature nor a convertible DER <c>SEQUENCE{r,s}</c>.
    /// </summary>
    internal static Result<byte[]> NormalizeToP1363(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        // Sniff #1: already the fixed-width P1363 the verifier consumes — accept, canonicalized to low-S.
        if (signature.Length == P1363Length)
            return EcdsaLowS.Canonicalize(signature);

        // Sniff #2: an ASN.1 DER SEQUENCE (the SignServer/BouncyCastle default) begins with tag 0x30.
        if (signature.Length >= 2 && signature[0] == 0x30)
        {
            try
            {
                var reader = new AsnReader(signature, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();
                ReadOnlySpan<byte> r = sequence.ReadIntegerBytes().Span;
                ReadOnlySpan<byte> s = sequence.ReadIntegerBytes().Span;
                sequence.ThrowIfNotEmpty();
                reader.ThrowIfNotEmpty();

                var p1363 = new byte[P1363Length];
                if (TryCopyCoordinate(r, p1363.AsSpan(0, CoordinateLength))
                    && TryCopyCoordinate(s, p1363.AsSpan(CoordinateLength, CoordinateLength)))
                {
                    return EcdsaLowS.Canonicalize(p1363);
                }
            }
            catch (AsnContentException)
            {
                // Falls through to the fail-loud path below.
            }

            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                "SGN022: SignServer returned a DER ECDSA signature that could not be converted to P1363 " +
                "(malformed SEQUENCE or an r/s value wider than a P-256 coordinate).");
        }

        return Result<byte[]>.Failure(ErrorKind.SecurityError,
            $"SGN022: SignServer returned an ECDSA signature in an unrecognized encoding " +
            $"({signature.Length} bytes; expected a 64-byte P1363 signature or a DER SEQUENCE).");
    }

    /// <summary>
    /// Left-pads a DER INTEGER's magnitude into a fixed 32-byte coordinate slot, dropping the leading
    /// sign byte DER prepends when the high bit is set. Fails if the magnitude is wider than 32 bytes
    /// (not a P-256 coordinate).
    /// </summary>
    private static bool TryCopyCoordinate(ReadOnlySpan<byte> value, Span<byte> destination)
    {
        var start = 0;
        while (start < value.Length && value[start] == 0)
            start++;

        var length = value.Length - start;
        if (length > destination.Length)
            return false;

        destination.Clear();
        value.Slice(start).CopyTo(destination.Slice(destination.Length - length));
        return true;
    }
}
