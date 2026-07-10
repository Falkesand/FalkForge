using System.Formats.Asn1;
using System.Security.Cryptography;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Signing.SignServer.Tests;

/// <summary>
/// Pins the DER→P1363 normalization that keeps the SignServer boundary honest. SignServer's PlainSigner
/// returns an ASN.1 DER <c>SEQUENCE{INTEGER r, INTEGER s}</c>; the FalkForge integrity verifier only accepts
/// fixed-width IEEE&#160;P1363 (r‖s, 64 bytes). A wrong conversion (swapped r/s, mis-padding, dropped sign
/// byte) would either change the signature length or make it fail cryptographic verification — both asserted
/// here, so a broken converter cannot pass. The byte-sniff guard is pinned to fail loud on anything that is
/// neither a 64-byte P1363 value nor a convertible DER sequence.
/// </summary>
public sealed class EcdsaSignatureFormatConverterTests
{
    private static readonly byte[] Message = System.Text.Encoding.UTF8.GetBytes("{\"canonical\":\"payload\"}");

    [Fact]
    public void RealDerSignature_ConvertsToP1363_ThatVerifiesWithTheSameKey()
    {
        // The strongest guard: a genuinely DER-encoded ECDSA signature, once normalized, must still verify
        // against its own key using the DEFAULT (P1363) VerifyHash — the exact call the engine verifier makes.
        // A converter that swaps r/s or mis-pads produces bytes that VerifyHash rejects.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var hash = SHA256.HashData(Message);
        var der = key.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal(0x30, der[0]); // sanity: it really is a DER SEQUENCE

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(der);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(64, result.Value.Length);
        Assert.True(key.VerifyHash(hash, result.Value)); // default format == P1363
    }

    [Fact]
    public void AlreadyP1363_IsCanonicalizedToLowS_AndStillVerifies()
    {
        // Was "passed through unchanged" before the anti-malleability hardening: the converter now also
        // canonicalizes to low-S (s ≤ n/2), because the verifier rejects high-S signatures. r must stay
        // byte-identical and the result must still verify with the same key.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var hash = SHA256.HashData(Message);
        var p1363 = key.SignHash(hash); // default overload emits P1363; high-S about half the time
        Assert.Equal(64, p1363.Length);
        var expectedR = p1363.AsSpan(0, 32).ToArray();

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(p1363);

        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Value.Length);
        Assert.Equal(expectedR, result.Value.AsSpan(0, 32).ToArray());
        Assert.True(FalkForge.Signing.EcdsaLowS.IsCanonical(result.Value));
        Assert.True(key.VerifyHash(hash, result.Value));
    }

    [Fact]
    public void HighSDerInput_YieldsCanonicalLowSP1363()
    {
        // Deterministic anti-malleability vector: a DER SEQUENCE carrying s = n − 5 (high-S) must come
        // out as P1363 with s = 5 (the canonical twin), r untouched. SignServer's response encoding is
        // outside FalkForge's control, so the boundary itself must guarantee the canonical form.
        var order = System.Numerics.BigInteger.Parse(
            "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        var rMagnitude = new byte[32];
        rMagnitude[0] = 0x11;
        rMagnitude[31] = 0x2A;
        var highS = (order - 5).ToByteArray(isUnsigned: true, isBigEndian: true);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteIntegerUnsigned(rMagnitude);
            writer.WriteIntegerUnsigned(highS);
        }
        var der = writer.Encode();

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(der);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var p1363 = result.Value;
        Assert.Equal(64, p1363.Length);
        Assert.Equal(rMagnitude, p1363.AsSpan(0, 32).ToArray());

        var expectedS = new byte[32];
        expectedS[31] = 0x05; // n − (n − 5) = 5, left-padded into the 32-byte slot
        Assert.Equal(expectedS, p1363.AsSpan(32).ToArray());
        Assert.True(FalkForge.Signing.EcdsaLowS.IsCanonical(p1363));
    }

    [Fact]
    public void DerWithHighBitR_AndShortS_IsLeftPaddedAndSignByteStripped()
    {
        // Edge cases that break naive converters:
        //  * r is a full 32-byte magnitude with the high bit set → DER prepends a 0x00 sign byte (33 encoded
        //    content bytes). The sign byte MUST be stripped, leaving the 32-byte magnitude verbatim.
        //  * s is a single byte → it MUST be left-padded into the low end of its 32-byte slot.
        var rMagnitude = new byte[32];
        rMagnitude[0] = 0x80; // high bit set → forces the DER sign byte
        rMagnitude[31] = 0x2A;
        var sMagnitude = new byte[] { 0x01 };

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteIntegerUnsigned(rMagnitude);
            writer.WriteIntegerUnsigned(sMagnitude);
        }
        var der = writer.Encode();

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(der);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var p1363 = result.Value;
        Assert.Equal(64, p1363.Length);

        var expected = new byte[64];
        rMagnitude.CopyTo(expected, 0);        // r fills slot [0..32) verbatim (sign byte dropped)
        expected[63] = 0x01;                   // s left-padded: 31 zero bytes then 0x01
        Assert.Equal(expected, p1363);
    }

    [Fact]
    public void DerCoordinateWiderThanP256_FailsLoud()
    {
        // A 33-byte magnitude with no leading zero is not a P-256 coordinate — must not be silently truncated.
        var oversized = new byte[33];
        for (var i = 0; i < oversized.Length; i++)
            oversized[i] = 0x7F; // no leading zero to strip; 33 real bytes > 32

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteIntegerUnsigned(oversized);
            writer.WriteIntegerUnsigned(new byte[] { 0x01 });
        }
        var der = writer.Encode();

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(der);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN022", result.Error.Message);
    }

    [Fact]
    public void GarbageNotDerNor64Bytes_FailsLoud()
    {
        var garbage = new byte[40];
        RandomNumberGenerator.Fill(garbage);
        garbage[0] = 0x11; // definitely not a DER SEQUENCE tag and not 64 bytes

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(garbage);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN022", result.Error.Message);
    }

    [Fact]
    public void MalformedDerSequence_FailsLoud()
    {
        // Starts with the DER tag 0x30 but the length/content is truncated garbage → AsnReader throws,
        // and the converter must map that to a fail-loud result rather than an escaping exception.
        var malformed = new byte[] { 0x30, 0x20, 0x02, 0x05, 0xDE, 0xAD };

        var result = EcdsaSignatureFormatConverter.NormalizeToP1363(malformed);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN022", result.Error.Message);
    }
}
