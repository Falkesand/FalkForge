using System.Security.Cryptography;

namespace FalkForge.Signing;

/// <summary>
/// The post-quantum <see cref="ISignatureProvider"/> (PQ-hybrid Stage 1): signs with an ML-DSA
/// (FIPS 204) private key loaded from a local PKCS#8 PEM file. The sibling of
/// <see cref="PemSignatureProvider"/> — same seam, different algorithm-native step.
///
/// <para><b>Contract.</b> Unlike ECDSA (which hashes the message with SHA-256 and signs the hash),
/// pure ML-DSA signs the raw canonical message bytes directly, bound to the frozen manifest context
/// string <see cref="SignatureAlgorithms.ManifestContext"/> for domain separation. The resulting
/// <see cref="ProviderSignature.Algorithm"/> carries the key's FIPS 204 parameter-set name
/// (e.g. <c>"ML-DSA-65"</c>) so the envelope assembler can tag the wire entry. The key is imported
/// per call and disposed immediately, like the ECDSA twin.</para>
///
/// <para><b>No fallback on the build machine.</b> When the OS/CNG cannot do ML-DSA
/// (<see cref="MLDsa.IsSupported"/> is false) signing fails loud (SGN011) — build machines are
/// controlled infrastructure and post-quantum signing silently not happening would be a lie in the
/// produced artifact. (Verification-side OS incapability is a separate, engine-side policy.)</para>
/// </summary>
public sealed class MLDsaPemSignatureProvider : ISignatureProvider
{
    private readonly string? _keyPath;
    private readonly string? _pemContent;

    /// <summary>Creates a provider that signs with the ML-DSA private key in the PEM file at <paramref name="keyPath"/>.</summary>
    public MLDsaPemSignatureProvider(string keyPath) : this(keyPath, pemContent: null)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPath);
    }

    private MLDsaPemSignatureProvider(string? keyPath, string? pemContent)
    {
        _keyPath = keyPath;
        _pemContent = pemContent;
    }

    /// <summary>
    /// Creates a provider that signs with ML-DSA private-key PEM supplied in memory (e.g. sourced from
    /// an environment variable so the key material never touches disk). Signing behavior is identical
    /// to the file-based provider.
    /// </summary>
    public static MLDsaPemSignatureProvider FromPemContent(string pemContent)
    {
        ArgumentException.ThrowIfNullOrEmpty(pemContent);
        return new MLDsaPemSignatureProvider(keyPath: null, pemContent);
    }

    /// <inheritdoc />
    public ValueTask<Result<ProviderSignature>> SignAsync(
        ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (!MLDsa.IsSupported)
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError,
                "SGN011: ML-DSA signing is not supported by this build machine's OS/cryptographic " +
                "platform. Post-quantum signing has no fallback — build on an OS with ML-DSA support."));

        // Same secret-hygiene contract as the ECDSA twin: the key path can originate from user config
        // where a mispasted secret would be echoed into CI logs — name the source, never the value.
        if (_keyPath is not null && !File.Exists(_keyPath))
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, "SGN002: Signing key file not found at the configured signing key path."));

        try
        {
            // Deliberately synchronous, mirroring PemSignatureProvider: the local provider completes
            // without blocking and returns an already-completed ValueTask.
#pragma warning disable CA1849 // Synchronous read is intentional for the non-blocking local provider.
            using var mldsa = MLDsa.ImportFromPem(_pemContent ?? File.ReadAllText(_keyPath!));
#pragma warning restore CA1849

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            // Both sources are secret-adjacent — never echo the path or the PEM content.
            var source = _keyPath is not null ? "the configured signing key file" : "in-memory PEM content";
            return new ValueTask<Result<ProviderSignature>>(Result<ProviderSignature>.Failure(
                ErrorKind.SecurityError, $"SGN002: Failed to load ML-DSA signing key from {source}: {ex.Message}"));
        }
    }
}
