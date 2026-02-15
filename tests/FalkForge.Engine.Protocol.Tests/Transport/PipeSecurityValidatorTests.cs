using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Transport;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Transport;

public class PipeSecurityValidatorTests
{
    [Fact]
    public void GenerateNonce_ReturnsCorrectSize()
    {
        var nonce = PipeSecurityValidator.GenerateNonce();

        Assert.Equal(PipeSecurityValidator.NonceSize, nonce.Length);
    }

    [Fact]
    public void GenerateNonce_ProducesDifferentValues()
    {
        var nonce1 = PipeSecurityValidator.GenerateNonce();
        var nonce2 = PipeSecurityValidator.GenerateNonce();

        Assert.False(nonce1.AsSpan().SequenceEqual(nonce2));
    }

    [Fact]
    public void ComputeHmac_ReturnsCorrectSize()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();

        var hmac = PipeSecurityValidator.ComputeHmac(secret, nonce);

        Assert.Equal(PipeSecurityValidator.HmacSize, hmac.Length);
    }

    [Fact]
    public void ComputeHmac_IsDeterministic_ForSameInputs()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();

        var hmac1 = PipeSecurityValidator.ComputeHmac(secret, nonce);
        var hmac2 = PipeSecurityValidator.ComputeHmac(secret, nonce);

        Assert.True(hmac1.AsSpan().SequenceEqual(hmac2));
    }

    [Fact]
    public void ComputeHmac_DiffersForDifferentSecrets()
    {
        var secret1 = RandomNumberGenerator.GetBytes(32);
        var secret2 = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();

        var hmac1 = PipeSecurityValidator.ComputeHmac(secret1, nonce);
        var hmac2 = PipeSecurityValidator.ComputeHmac(secret2, nonce);

        Assert.False(hmac1.AsSpan().SequenceEqual(hmac2));
    }

    [Fact]
    public void ComputeHmac_DiffersForDifferentNonces()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var nonce1 = PipeSecurityValidator.GenerateNonce();
        var nonce2 = PipeSecurityValidator.GenerateNonce();

        var hmac1 = PipeSecurityValidator.ComputeHmac(secret, nonce1);
        var hmac2 = PipeSecurityValidator.ComputeHmac(secret, nonce2);

        Assert.False(hmac1.AsSpan().SequenceEqual(hmac2));
    }

    [Fact]
    public void ValidateHmac_ReturnsTrue_ForCorrectHmac()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();
        var hmac = PipeSecurityValidator.ComputeHmac(secret, nonce);

        var result = PipeSecurityValidator.ValidateHmac(secret, nonce, hmac);

        Assert.True(result);
    }

    [Fact]
    public void ValidateHmac_ReturnsFalse_ForWrongSecret()
    {
        var secret1 = RandomNumberGenerator.GetBytes(32);
        var secret2 = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();
        var hmac = PipeSecurityValidator.ComputeHmac(secret1, nonce);

        var result = PipeSecurityValidator.ValidateHmac(secret2, nonce, hmac);

        Assert.False(result);
    }

    [Fact]
    public void ValidateHmac_ReturnsFalse_ForTamperedHmac()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var nonce = PipeSecurityValidator.GenerateNonce();
        var hmac = PipeSecurityValidator.ComputeHmac(secret, nonce);

        // Tamper with the HMAC
        hmac[0] ^= 0xFF;

        var result = PipeSecurityValidator.ValidateHmac(secret, nonce, hmac);

        Assert.False(result);
    }
}
