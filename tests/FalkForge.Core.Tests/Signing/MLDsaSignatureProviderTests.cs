using System.Security.Cryptography;
using System.Text;
using FalkForge;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Core.Tests.Signing;

/// <summary>
/// The post-quantum half of the hybrid signing scheme (PQ Stage 1): <see cref="MLDsaPemSignatureProvider"/>
/// signs the SAME canonical message bytes the ECDSA providers sign, but pure ML-DSA (FIPS 204) over the raw
/// message with the frozen context string <c>"falkforge/manifest"</c> — no SHA-256 pre-hash, no P1363, no
/// low-S. These tests pin that contract (context binding included: a signature made under any other context
/// must not verify) and the <see cref="ProviderSignature.Algorithm"/> seam that lets the envelope assembler
/// dispatch per algorithm without a second interface.
/// </summary>
public sealed class MLDsaSignatureProviderTests : IDisposable
{
    private readonly string _tempDir;

    public MLDsaSignatureProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"MLDsaProviderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static readonly byte[] Message = Encoding.UTF8.GetBytes("{\"canonical\":\"message\"}");

    [Fact]
    public void ProviderSignature_Algorithm_DefaultsToEcdsaP256()
    {
        // The defaulted Algorithm keeps every existing provider (including third-party
        // ISignatureProvider implementations compiled against the old shape) meaning exactly what it
        // meant: an ECDSA-P256 signature.
        var signature = new ProviderSignature
        {
            SubjectPublicKeyInfo = [1, 2, 3],
            Signature = [4, 5, 6]
        };

        Assert.Equal(SignatureAlgorithms.EcdsaP256, signature.Algorithm);
        Assert.Equal("ECDSA-P256", signature.Algorithm);
    }

    [Fact]
    public void ManifestContext_IsTheFrozenValue()
    {
        // The ML-DSA context string is part of the wire/trust contract forever once the first hybrid
        // bundle ships. Pin the exact bytes.
        Assert.Equal("falkforge/manifest"u8.ToArray(), SignatureAlgorithms.ManifestContext.ToArray());
        Assert.Equal("ML-DSA-65", SignatureAlgorithms.MlDsa65);
    }

    [Fact]
    public async Task MLDsaPemSignatureProvider_SignsRawMessage_VerifiableUnderManifestContext()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        using var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var pemPath = Path.Combine(_tempDir, "pq.pem");
        await File.WriteAllTextAsync(pemPath, key.ExportPkcs8PrivateKeyPem());

        var result = await new MLDsaPemSignatureProvider(pemPath).SignAsync(Message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var signature = result.Value;
        Assert.Equal(SignatureAlgorithms.MlDsa65, signature.Algorithm);
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), signature.SubjectPublicKeyInfo);
        Assert.Equal(3309, signature.Signature.Length); // FIPS 204 ML-DSA-65 signature size

        using var pub = MLDsa.ImportSubjectPublicKeyInfo(signature.SubjectPublicKeyInfo);
        Assert.True(pub.VerifyData(Message, signature.Signature, SignatureAlgorithms.ManifestContext));
    }

    [Fact]
    public async Task MLDsaPemSignatureProvider_SignatureDoesNotVerifyUnderWrongContext()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Context-string domain separation: a manifest signature must never be replayable as any
        // other FalkForge ML-DSA artifact signature (or verify context-free).
        using var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var result = await MLDsaPemSignatureProvider.FromPemContent(key.ExportPkcs8PrivateKeyPem())
            .SignAsync(Message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        using var pub = MLDsa.ImportSubjectPublicKeyInfo(result.Value.SubjectPublicKeyInfo);
        Assert.False(pub.VerifyData(Message, result.Value.Signature, "falkforge/other"u8));
        Assert.False(pub.VerifyData(Message, result.Value.Signature));
    }

    [Fact]
    public async Task MLDsaPemSignatureProvider_MissingFile_FailsWithSgn002_WithoutEchoingKeyPath()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        // Same secret-hygiene contract as the ECDSA twin: the key path can be a mispasted secret
        // from user config; the error names the source, never the configured value.
        const string secretShapedName = "ghp_FakeLeakCanary0123456789abcdef";
        var result = await new MLDsaPemSignatureProvider(Path.Combine(_tempDir, secretShapedName))
            .SignAsync(Message);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN002", result.Error.Message);
        Assert.DoesNotContain(secretShapedName, result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MLDsaPemSignatureProvider_InvalidPemContent_FailsWithSgn002_WithoutEchoingContent()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        const string bogus = "not-a-pem-private-key";
        var result = await MLDsaPemSignatureProvider.FromPemContent(bogus).SignAsync(Message);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN002", result.Error.Message);
        Assert.DoesNotContain(bogus, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MLDsaPemSignatureProvider_CompletesSynchronously()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        using var key = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        var task = MLDsaPemSignatureProvider.FromPemContent(key.ExportPkcs8PrivateKeyPem()).SignAsync(Message);

        // Local crypto must not go async — the sync build pipeline relies on this to avoid blocking.
        Assert.True(task.IsCompleted);
        _ = await task;
    }

    [Fact]
    public async Task EphemeralMLDsaSignatureProvider_ProducesFreshVerifiableSignatures()
    {
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        var first = await new EphemeralMLDsaSignatureProvider().SignAsync(Message);
        var second = await new EphemeralMLDsaSignatureProvider().SignAsync(Message);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Message : null);
        Assert.Equal(SignatureAlgorithms.MlDsa65, first.Value.Algorithm);

        // Each call generates a throwaway key, so the public keys differ across builds.
        Assert.NotEqual(first.Value.SubjectPublicKeyInfo, second.Value.SubjectPublicKeyInfo);

        using var pub = MLDsa.ImportSubjectPublicKeyInfo(first.Value.SubjectPublicKeyInfo);
        Assert.True(pub.VerifyData(Message, first.Value.Signature, SignatureAlgorithms.ManifestContext));
    }
}
