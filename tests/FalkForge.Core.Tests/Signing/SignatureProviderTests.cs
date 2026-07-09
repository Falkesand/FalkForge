using System.Security.Cryptography;
using System.Text;
using FalkForge;
using FalkForge.Signing;
using Xunit;

namespace FalkForge.Core.Tests.Signing;

/// <summary>
/// Pins the encoding contract of the built-in <see cref="ISignatureProvider"/> implementations: the
/// signature is produced over <c>SHA-256(message)</c> in IEEE P1363 (r‖s) form — the exact encoding the
/// engine's verifier consumes. This is a behavior-preserving guard for the C17 provider refactor and the
/// normalization target a future remote (DER-emitting) backend must convert to.
/// </summary>
public sealed class SignatureProviderTests : IDisposable
{
    private readonly string _tempDir;

    public SignatureProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SigProviderTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static readonly byte[] Message = Encoding.UTF8.GetBytes("{\"canonical\":\"message\"}");

    [Fact]
    public async Task PemSignatureProvider_SignsInP1363_AndVerifiesWithTheEmbeddedKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "k.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        var result = await new PemSignatureProvider(pemPath).SignAsync(Message);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var signature = result.Value;

        // The public key round-trips to the same key that produced the signature.
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), signature.SubjectPublicKeyInfo);

        // P1363 (r‖s) for P-256 is exactly 64 bytes — a DER (Rfc3279DerSequence) signature would be ~70–72
        // bytes and variable length. This length assertion is what locks the encoding.
        Assert.Equal(64, signature.Signature.Length);

        // VerifyHash with the DEFAULT format expects P1363 — the same call the runtime verifier makes.
        using var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(signature.SubjectPublicKeyInfo, out _);
        Assert.True(pub.VerifyHash(SHA256.HashData(Message), signature.Signature));
    }

    [Fact]
    public async Task PemSignatureProvider_MissingFile_FailsWithSgn002()
    {
        var result = await new PemSignatureProvider(Path.Combine(_tempDir, "nope.pem")).SignAsync(Message);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("SGN002", result.Error.Message);
    }

    [Fact]
    public async Task PemSignatureProvider_CompletesSynchronously()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "sync.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        // Local crypto must not go async — the sync build pipeline relies on this to avoid blocking.
        var task = new PemSignatureProvider(pemPath).SignAsync(Message);
        Assert.True(task.IsCompleted);
        _ = await task;
    }

    [Fact]
    public async Task EphemeralSignatureProvider_ProducesFreshVerifiableP1363Signatures()
    {
        var first = await new EphemeralSignatureProvider().SignAsync(Message);
        var second = await new EphemeralSignatureProvider().SignAsync(Message);

        Assert.True(first.IsSuccess);
        Assert.Equal(64, first.Value.Signature.Length);

        // Each call generates a throwaway key, so the public keys differ across builds.
        Assert.NotEqual(first.Value.SubjectPublicKeyInfo, second.Value.SubjectPublicKeyInfo);

        using var pub = ECDsa.Create();
        pub.ImportSubjectPublicKeyInfo(first.Value.SubjectPublicKeyInfo, out _);
        Assert.True(pub.VerifyHash(SHA256.HashData(Message), first.Value.Signature));
    }
}
