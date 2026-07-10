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
    private readonly string? _keyPath;
    private readonly string? _pemContent;

    /// <summary>Creates a provider that signs with the ECDSA private key in the PEM file at <paramref name="keyPath"/>.</summary>
    public PemSignatureProvider(string keyPath) : this(keyPath, pemContent: null)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPath);
    }

    private PemSignatureProvider(string? keyPath, string? pemContent)
    {
        _keyPath = keyPath;
        _pemContent = pemContent;
    }

    /// <summary>
    /// Creates a provider that signs with ECDSA private-key PEM supplied in memory — the shape used when
    /// the key is sourced from an environment variable (e.g. by <c>forge build</c>'s signing config) so
    /// the key material never has to touch disk. Signing behavior is identical to the file-based provider.
    /// </summary>
    public static PemSignatureProvider FromPemContent(string pemContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(pemContent);
        return new PemSignatureProvider(keyPath: null, pemContent);
    }

    /// <inheritdoc />
    public ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        // Local crypto completes synchronously — the abstraction is async for remote backends, but this
        // path never blocks and returns an already-completed ValueTask.
        if (_keyPath is not null && !File.Exists(_keyPath))
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, $"SGN002: Signing key file not found at '{_keyPath}'."));

        try
        {
            using var ecdsa = ECDsa.Create();
            // Deliberately synchronous: the local-PEM provider completes without blocking and returns an
            // already-completed ValueTask so the sync build pipeline never has to await real I/O. The async
            // signature exists for remote backends, not for this reading a small local key file.
#pragma warning disable CA1849 // Synchronous read is intentional for the non-blocking local provider.
            ecdsa.ImportFromPem(_pemContent ?? File.ReadAllText(_keyPath!));
#pragma warning restore CA1849
            return new ValueTask<Result<ProviderSignature>>(
                EcdsaSignatureHelper.Sign(ecdsa, message.Span, keyId: string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            // In-memory PEM is secret key material — the error must name the source without echoing it.
            var source = _keyPath is not null ? $"'{_keyPath}'" : "in-memory PEM content";
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, $"SGN002: Failed to load signing key from {source}: {ex.Message}"));
        }
    }
}
