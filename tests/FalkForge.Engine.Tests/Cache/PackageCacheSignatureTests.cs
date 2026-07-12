namespace FalkForge.Engine.Tests.Cache;

using FalkForge;
using FalkForge.Engine.Cache;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class PackageCacheSignatureTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CacheLayout _layout;
    private readonly Guid _bundleId = Guid.NewGuid();

    public PackageCacheSignatureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkForge_CacheSigTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _layout = new CacheLayout(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Cache_WithThumbprint_ValidatesSignature()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var package = CreatePackage(sourceFile, authenticodeThumbprint: "ABC123");

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal("ABC123", validator.LastThumbprint);
    }

    [Fact]
    public void Cache_WithoutThumbprint_SkipsValidation()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var package = CreatePackage(sourceFile, authenticodeThumbprint: null);

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public void Cache_SignatureInvalid_ReturnsFailure()
    {
        var validator = new MockAuthenticodeValidator().ReturnsFailure("Signature verification failed");
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var package = CreatePackage(sourceFile, authenticodeThumbprint: "BAD_THUMB");

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("Signature verification failed", result.Error.Message);
    }

    [Fact]
    public void Cache_WithPublicKeyPin_ValidatesSignature()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var pin = new string('A', 64);
        var package = CreatePackage(sourceFile, authenticodeThumbprint: null, remotePayloadCertificatePublicKey: pin);

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, validator.CallCount);
        Assert.Equal(pin, validator.LastPublicKeyHash);
        // The signature is verified against the cached target file, not the source — no TOCTOU gap.
        Assert.Equal(result.Value, validator.LastFilePath);
    }

    [Fact]
    public void Cache_PublicKeyPin_InvalidSignature_ReturnsFailureAndDeletesPayload()
    {
        var validator = new MockAuthenticodeValidator().ReturnsFailure("public-key pin mismatch");
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var package = CreatePackage(sourceFile, authenticodeThumbprint: null,
            remotePayloadCertificatePublicKey: new string('B', 64));

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("public-key pin mismatch", result.Error.Message);
        // Fail closed: a payload that fails the pin must not remain in the cache.
        Assert.False(File.Exists(_layout.GetPayloadPath(_bundleId, package.Id, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public void Cache_PinSet_NoValidator_FailsClosed()
    {
        // A pin is authored but the engine has no Authenticode validator (e.g. a non-Windows build).
        // The payload must be rejected rather than silently trusted.
        var cache = new PackageCache(_layout, authenticodeValidator: null);
        var sourceFile = CreateTempFile("test content");
        var package = CreatePackage(sourceFile, authenticodeThumbprint: null,
            remotePayloadCertificatePublicKey: new string('C', 64));

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("no signature validator", result.Error.Message);
        Assert.False(File.Exists(_layout.GetPayloadPath(_bundleId, package.Id, Path.GetFileName(sourceFile))));
    }

    [Fact]
    public void Cache_ThumbprintAndPin_BothForwardedToValidator()
    {
        var validator = new MockAuthenticodeValidator().ReturnsSuccess();
        var cache = new PackageCache(_layout, validator);
        var sourceFile = CreateTempFile("test content");
        var pin = new string('D', 64);
        var package = CreatePackage(sourceFile, authenticodeThumbprint: "ABC123",
            remotePayloadCertificatePublicKey: pin);

        var result = cache.CachePackage(_bundleId, package, sourceFile);

        Assert.True(result.IsSuccess);
        Assert.Equal("ABC123", validator.LastThumbprint);
        Assert.Equal(pin, validator.LastPublicKeyHash);
    }

    private string CreateTempFile(string content)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.msi");
        File.WriteAllText(path, content);
        return path;
    }

    private PackageInfo CreatePackage(
        string sourceFile,
        string? authenticodeThumbprint,
        string? remotePayloadCertificatePublicKey = null)
    {
        using var stream = File.OpenRead(sourceFile);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));

        return new PackageInfo
        {
            Id = "TestPackage",
            Type = PackageType.MsiPackage,
            DisplayName = "Test Package",
            SourcePath = sourceFile,
            Sha256Hash = hash,
            AuthenticodeThumbprint = authenticodeThumbprint,
            RemotePayloadCertificatePublicKey = remotePayloadCertificatePublicKey
        };
    }
}
