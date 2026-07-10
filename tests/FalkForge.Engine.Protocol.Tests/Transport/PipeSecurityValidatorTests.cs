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

    // --- Mutual-auth proof primitives (domain separation) ---
    // The old one-directional ComputeHmac/ValidateHmac tests were removed together with those
    // primitives; the proof-based tests below cover the surviving handshake surface.

    [Fact]
    public void ComputeProof_ReturnsHmacSize()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var serverNonce = PipeSecurityValidator.GenerateNonce();
        var clientNonce = PipeSecurityValidator.GenerateNonce();

        var proof = PipeSecurityValidator.ComputeProof(
            secret, PipeSecurityValidator.ClientProofLabel, serverNonce, clientNonce);

        Assert.Equal(PipeSecurityValidator.HmacSize, proof.Length);
    }

    [Fact]
    public void ComputeProof_ValidatesRoundTrip_ForMatchingInputs()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var serverNonce = PipeSecurityValidator.GenerateNonce();
        var clientNonce = PipeSecurityValidator.GenerateNonce();

        var proof = PipeSecurityValidator.ComputeProof(
            secret, PipeSecurityValidator.ServerProofLabel, serverNonce, clientNonce);

        Assert.True(PipeSecurityValidator.ValidateProof(
            secret, PipeSecurityValidator.ServerProofLabel, serverNonce, clientNonce, proof));
    }

    [Fact]
    public void ClientAndServerLabels_AreDistinct()
    {
        // Domain separation depends on the two labels differing; if a refactor ever made them
        // equal, the reflection defense silently collapses. Encode that invariant as a test.
        Assert.False(
            PipeSecurityValidator.ClientProofLabel.SequenceEqual(PipeSecurityValidator.ServerProofLabel));
    }

    [Fact]
    public void ClientProof_CannotBeReflectedAsServerProof()
    {
        // The reflection attack: a rogue server echoes the client's own tag_c back as tag_s.
        // Because the client's proof binds LABEL_C2S and the server's proof binds LABEL_S2C,
        // the reflected tag must NOT validate as a server proof over the same nonces.
        var secret = RandomNumberGenerator.GetBytes(32);
        var serverNonce = PipeSecurityValidator.GenerateNonce();
        var clientNonce = PipeSecurityValidator.GenerateNonce();

        var clientProof = PipeSecurityValidator.ComputeProof(
            secret, PipeSecurityValidator.ClientProofLabel, serverNonce, clientNonce);

        var reflectedIsAcceptedAsServerProof = PipeSecurityValidator.ValidateProof(
            secret, PipeSecurityValidator.ServerProofLabel, serverNonce, clientNonce, clientProof);

        Assert.False(reflectedIsAcceptedAsServerProof);
    }

    [Fact]
    public void ValidateProof_ReturnsFalse_ForWrongSecret()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        var wrongSecret = RandomNumberGenerator.GetBytes(32);
        var serverNonce = PipeSecurityValidator.GenerateNonce();
        var clientNonce = PipeSecurityValidator.GenerateNonce();

        var proof = PipeSecurityValidator.ComputeProof(
            secret, PipeSecurityValidator.ServerProofLabel, serverNonce, clientNonce);

        Assert.False(PipeSecurityValidator.ValidateProof(
            wrongSecret, PipeSecurityValidator.ServerProofLabel, serverNonce, clientNonce, proof));
    }
}
