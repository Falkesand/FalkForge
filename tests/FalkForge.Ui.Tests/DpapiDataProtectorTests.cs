namespace FalkForge.Ui.Tests;

using System.Security.Cryptography;
using FalkForge.Ui.Abstractions;
using Xunit;

// All tests in this file target net10.0-windows (project TFM).
// DPAPI (ProtectedData) is available on Windows only — the TFM constraint means the
// entire project is already Windows-gated, so no per-test skip attribute is needed.
public sealed class DpapiDataProtectorTests
{
    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Protect_and_Unprotect_roundtrips()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Protect_returns_different_bytes_than_input()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        var encrypted = protector.Protect(original);

        Assert.NotEqual(original, encrypted);
    }

    [Fact]
    public void Protect_empty_array_roundtrips()
    {
        var protector = new DpapiDataProtector();

        var encrypted = protector.Protect([]);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Empty(decrypted);
    }

    [Fact]
    public void Protect_large_payload_roundtrips()
    {
        var protector = new DpapiDataProtector();
        var original = new byte[4096];
        Random.Shared.NextBytes(original);

        var encrypted = protector.Protect(original);
        var decrypted = protector.Unprotect(encrypted);

        Assert.Equal(original, decrypted);
    }

    // ── Corrupt / invalid ciphertext ─────────────────────────────────────────

    [Fact]
    public void Unprotect_random_bytes_throws_CryptographicException()
    {
        // DPAPI cannot decrypt arbitrary random bytes.
        var protector = new DpapiDataProtector();
        var junk = new byte[64];
        Random.Shared.NextBytes(junk);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(junk));
    }

    [Fact]
    public void Unprotect_truncated_ciphertext_throws_CryptographicException()
    {
        // Take only the first half of a valid encrypted blob — DPAPI header is corrupted.
        var protector = new DpapiDataProtector();
        var encrypted = protector.Protect(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        var truncated = encrypted[..(encrypted.Length / 2)];

        Assert.Throws<CryptographicException>(() => protector.Unprotect(truncated));
    }

    [Fact]
    public void Unprotect_empty_array_throws_CryptographicException()
    {
        // An empty byte[] is not a valid DPAPI blob.
        var protector = new DpapiDataProtector();

        Assert.Throws<CryptographicException>(() => protector.Unprotect([]));
    }

    [Fact]
    public void Unprotect_tampered_ciphertext_throws_CryptographicException()
    {
        // Flip a byte in the middle of the encrypted payload — DPAPI integrity check fails.
        var protector = new DpapiDataProtector();
        var original = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var encrypted = protector.Protect(original);

        // Flip a middle byte (avoid header bytes at the very start).
        var tampered = (byte[])encrypted.Clone();
        var midpoint = tampered.Length / 2;
        tampered[midpoint] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_single_byte_throws_CryptographicException()
    {
        // A single byte cannot be a valid DPAPI blob header.
        var protector = new DpapiDataProtector();

        Assert.Throws<CryptographicException>(() => protector.Unprotect(new byte[] { 0xAB }));
    }
}
