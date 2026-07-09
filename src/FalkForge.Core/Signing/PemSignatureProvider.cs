using System.Security.Cryptography;

namespace FalkForge.Signing;

/// <summary>
/// The built-in default <see cref="ISignatureProvider"/>: signs with an ECDSA private key loaded from a
/// local PEM file. A configured key gives a stable public key across builds (authorship proof), matching
/// the historical behavior of the integrity signer before the provider seam existed.
///
/// <para>Signing is the standard ECDSA step — <c>SignHash(SHA-256(message))</c> — which yields the IEEE
/// P1363 (r‖s) encoding the verifier expects. The key is imported per call and disposed immediately, so no
/// long-lived private-key material is retained on the provider.</para>
/// </summary>
public sealed class PemSignatureProvider : ISignatureProvider
{
    private readonly string _keyPath;

    /// <summary>Creates a provider that signs with the ECDSA private key in the PEM file at <paramref name="keyPath"/>.</summary>
    public PemSignatureProvider(string keyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPath);
        _keyPath = keyPath;
    }

    /// <inheritdoc />
    public ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        // Local crypto completes synchronously — the abstraction is async for remote backends, but this
        // path never blocks and returns an already-completed ValueTask.
        if (!File.Exists(_keyPath))
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, $"SGN002: Signing key file not found at '{_keyPath}'."));

        try
        {
            using var ecdsa = ECDsa.Create();
            // Deliberately synchronous: the local-PEM provider completes without blocking and returns an
            // already-completed ValueTask so the sync build pipeline never has to await real I/O. The async
            // signature exists for remote backends, not for this reading a small local key file.
#pragma warning disable CA1849 // Synchronous read is intentional for the non-blocking local provider.
            ecdsa.ImportFromPem(File.ReadAllText(_keyPath));
#pragma warning restore CA1849
            return new ValueTask<Result<ProviderSignature>>(
                EcdsaSignatureHelper.Sign(ecdsa, message.Span, keyId: string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, $"SGN002: Failed to load signing key from '{_keyPath}': {ex.Message}"));
        }
    }
}
