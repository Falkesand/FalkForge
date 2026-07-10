namespace FalkForge.Engine.Protocol.Transport;

using System.Security.Cryptography;

public static class PipeSecurityValidator
{
    public const int NonceSize = 32;
    public const int HmacSize = 32; // SHA-256 output

    // Domain-separation labels for the mutual handshake. The client and server bind
    // BOTH nonces into their proof, but under DISTINCT constant prefixes so a proof
    // computed by one party can never be replayed/reflected as the other party's proof
    // (mirror/reflection attack). LABEL_C2S proves "client knows the secret", LABEL_S2C
    // proves "server knows the secret". They MUST differ — that difference is the entire
    // defense against reflection when the transcript (both nonces) is otherwise symmetric.
    private static readonly byte[] ClientProofLabelBytes = "FalkForge-Elevation-Handshake-Proof-C2S-v1"u8.ToArray();
    private static readonly byte[] ServerProofLabelBytes = "FalkForge-Elevation-Handshake-Proof-S2C-v1"u8.ToArray();

    public static ReadOnlySpan<byte> ClientProofLabel => ClientProofLabelBytes;
    public static ReadOnlySpan<byte> ServerProofLabel => ServerProofLabelBytes;

    public static byte[] GenerateNonce()
    {
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    // NOTE: the old one-directional ComputeHmac/ValidateHmac primitives were removed once the
    // mutual handshake landed — they authenticated only one party and leaving them as public
    // API invited copy-paste of the insecure pattern. Use ComputeProof/ValidateProof.

    /// <summary>
    /// Computes a mutual-auth proof tag = HMAC-SHA256(secret, label || serverNonce || clientNonce).
    /// The label provides domain separation between the client's and server's proofs; both
    /// nonces are bound so neither party unilaterally controls the transcript.
    /// </summary>
    public static byte[] ComputeProof(
        byte[] sharedSecret,
        ReadOnlySpan<byte> label,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientNonce)
    {
        // label (<=64) + 32 + 32 stays well under 1KB: stackalloc the transcript to avoid
        // a heap allocation on this hot security path (Gate 6).
        var messageLength = label.Length + serverNonce.Length + clientNonce.Length;
        Span<byte> message = stackalloc byte[messageLength];
        label.CopyTo(message);
        serverNonce.CopyTo(message[label.Length..]);
        clientNonce.CopyTo(message[(label.Length + serverNonce.Length)..]);

        var tag = new byte[HmacSize];
        HMACSHA256.HashData(sharedSecret, message, tag);
        return tag;
    }

    /// <summary>
    /// Constant-time verification of a received proof tag against the locally recomputed value.
    /// </summary>
    public static bool ValidateProof(
        byte[] sharedSecret,
        ReadOnlySpan<byte> label,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> receivedProof)
    {
        Span<byte> expected = stackalloc byte[HmacSize];
        var messageLength = label.Length + serverNonce.Length + clientNonce.Length;
        Span<byte> message = stackalloc byte[messageLength];
        label.CopyTo(message);
        serverNonce.CopyTo(message[label.Length..]);
        clientNonce.CopyTo(message[(label.Length + serverNonce.Length)..]);

        HMACSHA256.HashData(sharedSecret, message, expected);
        return CryptographicOperations.FixedTimeEquals(expected, receivedProof);
    }
}
