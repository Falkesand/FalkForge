using System.Security.Cryptography;

namespace FalkForge.Signing;

/// <summary>
/// The post-quantum half of the zero-config ephemeral hybrid pair (PQ-hybrid Stage 1, human decision
/// §8.7): generates a throwaway ML-DSA-65 key per call and signs the canonical message with it under
/// the frozen manifest context. Paired with <see cref="EphemeralSignatureProvider"/> by the envelope
/// assembler so an unconfigured build carries the same hybrid envelope shape production uses, while
/// scoping any key compromise to a single build.
/// </summary>
public sealed class EphemeralMLDsaSignatureProvider : ISignatureProvider
{
    /// <inheritdoc />
    public ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (!MLDsa.IsSupported)
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError,
                "SGN011: ML-DSA signing is not supported by this build machine's OS/cryptographic " +
                "platform. Post-quantum signing has no fallback — build on an OS with ML-DSA support."));

        using var mldsa = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var signature = new byte[mldsa.Algorithm.SignatureSizeInBytes];
        mldsa.SignData(message.Span, signature, SignatureAlgorithms.ManifestContext);

        return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Success(
            new ProviderSignature
            {
                SubjectPublicKeyInfo = mldsa.ExportSubjectPublicKeyInfo(),
                Signature = signature,
                KeyId = string.Empty,
                Algorithm = mldsa.Algorithm.Name
            }));
    }
}
