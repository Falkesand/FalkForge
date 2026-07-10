using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Core.Tests.Signing;

/// <summary>
/// Pins the anti-malleability contract of the manifest signing path. ECDSA accepts both (r, s) and
/// its twin (r, n − s) over the same message, so without canonicalization an attacker can produce a
/// different-bytes-but-still-valid signature for a legitimately signed manifest. These tests encode
/// the two halves of the defense: <see cref="EcdsaLowS.Canonicalize"/> guarantees FalkForge only ever
/// EMITS low-S signatures (Windows CNG emits high-S roughly half the time, so this is a real
/// transformation, not a formality), and <see cref="EcdsaLowS.IsCanonical"/> is the predicate the
/// verifier uses to REJECT the high-S twin.
/// </summary>
public sealed class EcdsaLowSTests
{
    // P-256 group order n and floor(n/2), independent of the implementation under test.
    private static readonly BigInteger Order = BigInteger.Parse(
        "0FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
        NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly BigInteger HalfOrder = Order >> 1;

    private static readonly byte[] Message = System.Text.Encoding.UTF8.GetBytes("{\"files\":[\"payload\"]}");

    private static BigInteger ReadS(ReadOnlySpan<byte> p1363) =>
        new(p1363[32..], isUnsigned: true, isBigEndian: true);

    /// <summary>Builds a 64-byte P1363 signature with the given r filler byte and exact s value.</summary>
    private static byte[] BuildP1363(byte rFiller, BigInteger s)
    {
        var signature = new byte[64];
        signature.AsSpan(0, 32).Fill(rFiller);
        var sBytes = s.ToByteArray(isUnsigned: true, isBigEndian: true);
        sBytes.CopyTo(signature.AsSpan(64 - sBytes.Length));
        return signature;
    }

    [Fact]
    public void IsCanonical_HighS_IsFalse_LowS_IsTrue()
    {
        // s = n − 1 is the maximal valid high-S scalar; s = 1 and s = n/2 (the boundary) are canonical.
        Assert.False(EcdsaLowS.IsCanonical(BuildP1363(0x2A, Order - 1)));
        Assert.True(EcdsaLowS.IsCanonical(BuildP1363(0x2A, BigInteger.One)));
        Assert.True(EcdsaLowS.IsCanonical(BuildP1363(0x2A, HalfOrder)));
        Assert.False(EcdsaLowS.IsCanonical(BuildP1363(0x2A, HalfOrder + 1)));
    }

    [Fact]
    public void IsCanonical_NonP256Length_IsFalse_FailClosed()
    {
        // The system signs with P-256 exclusively (64-byte P1363). Any other length must never count
        // as canonical, so a future non-P-256 signature cannot silently bypass the malleability gate.
        Assert.False(EcdsaLowS.IsCanonical(new byte[96])); // P-384-sized
        Assert.False(EcdsaLowS.IsCanonical(new byte[63]));
        Assert.False(EcdsaLowS.IsCanonical([]));
    }

    [Fact]
    public void Canonicalize_HighS_FlipsToOrderMinusS_LeavingRUntouched()
    {
        // Deterministic vector: s = n − 5 must become exactly 5, with r byte-identical.
        var signature = BuildP1363(0x7E, Order - 5);
        var expectedR = signature.AsSpan(0, 32).ToArray();

        var canonical = EcdsaLowS.Canonicalize(signature);

        Assert.Equal(expectedR, canonical.AsSpan(0, 32).ToArray());
        Assert.Equal(new BigInteger(5), ReadS(canonical));
        Assert.True(EcdsaLowS.IsCanonical(canonical));
    }

    [Fact]
    public void Canonicalize_LowS_IsUnchanged_AndIdempotent()
    {
        var signature = BuildP1363(0x11, HalfOrder);
        var before = (byte[])signature.Clone();

        var once = EcdsaLowS.Canonicalize(signature);
        var twice = EcdsaLowS.Canonicalize(once);

        Assert.Equal(before, twice);
    }

    [Fact]
    public void Canonicalize_NonP256Length_ReturnsInputUnchanged()
    {
        // Not a P-256 P1363 value — leave it alone; the verifier rejects it as non-canonical.
        var input = new byte[96];
        input.AsSpan().Fill(0xFF);
        var before = (byte[])input.Clone();

        var result = EcdsaLowS.Canonicalize(input);

        Assert.Same(input, result);
        Assert.Equal(before, result);
    }

    [Fact]
    public void Canonicalize_PreservesCryptographicValidity()
    {
        // The flipped signature must still verify over the same message with the same key —
        // canonicalization changes bytes, never validity.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var hash = SHA256.HashData(Message);

        for (var i = 0; i < 16; i++)
        {
            var canonical = EcdsaLowS.Canonicalize(key.SignHash(hash));
            Assert.True(EcdsaLowS.IsCanonical(canonical));
            Assert.True(key.VerifyHash(hash, canonical));
        }
    }

    [Fact]
    public async Task PemProvider_AlwaysEmitsLowSSignatures()
    {
        // CNG emits high-S about half the time, so 24 signings from an uncanonicalized signer fail this
        // with overwhelming probability — the regression signal that the sign side really canonicalizes.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(Path.GetTempPath(), $"lowS_{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(pemPath, key.ExportPkcs8PrivateKeyPem());
        try
        {
            var provider = new PemSignatureProvider(pemPath);
            for (var i = 0; i < 24; i++)
            {
                var result = await provider.SignAsync(Message);
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
                Assert.True(
                    EcdsaLowS.IsCanonical(result.Value.Signature),
                    $"Signing round {i} produced a non-canonical (high-S) signature.");
            }
        }
        finally
        {
            File.Delete(pemPath);
        }
    }

    [Fact]
    public async Task EphemeralProvider_AlwaysEmitsLowSSignatures()
    {
        var provider = new EphemeralSignatureProvider();
        for (var i = 0; i < 24; i++)
        {
            var result = await provider.SignAsync(Message);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
            Assert.True(
                EcdsaLowS.IsCanonical(result.Value.Signature),
                $"Signing round {i} produced a non-canonical (high-S) signature.");
        }
    }
}
