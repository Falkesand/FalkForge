using System.Security.Cryptography;

namespace FalkForge.Signing;

/// <summary>
/// The zero-config fallback <see cref="ISignatureProvider"/>: generates a throwaway P-256 key per call and
/// signs with it. Used when no signing key is configured, giving tamper-evidence out of the box while
/// scoping any key compromise to a single build (each build's envelope carries a unique public key).
/// </summary>
public sealed class EphemeralSignatureProvider : ISignatureProvider
{
    /// <inheritdoc />
    public ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new ValueTask<Result<ProviderSignature>>(
            EcdsaSignatureHelper.Sign(ecdsa, message.Span, keyId: string.Empty));
    }
}
