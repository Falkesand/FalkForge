namespace FalkForge.Engine.Tests.Integrity;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class IntegrityVerifierTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrityVerifierTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"IntegrityTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static InstallerManifest CreateManifest(string? manifestSignature = null)
    {
        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [],
            ManifestSignature = manifestSignature
        };
    }

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return fullPath;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Creates a valid signed manifest signature JSON with real ECDSA keys.
    /// </summary>
    private static string CreateSignedEnvelope(
        ECDsa key,
        IReadOnlyList<ManifestFileEntry> files)
    {
        var filesJson = JsonSerializer.Serialize(files, IntegritySignatureContext.Default.IReadOnlyListManifestFileEntry);
        var filesBytes = Encoding.UTF8.GetBytes(filesJson);
        var hash = SHA256.HashData(filesBytes);
        var signature = key.SignHash(hash);

        var envelope = new ManifestSignatureEnvelope
        {
            Version = 1,
            Algorithm = "ECDSA-P256",
            PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            Files = files,
            Signature = Convert.ToBase64String(signature)
        };

        return JsonSerializer.Serialize(envelope, IntegritySignatureContext.Default.ManifestSignatureEnvelope);
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Verify_NullManifestSignature_ReturnsSuccess()
    {
        // Arrange
        var manifest = CreateManifest(manifestSignature: null);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_ValidSignatureAndMatchingFiles_ReturnsSuccess()
    {
        // Arrange
        var filePath = CreateFile("app.exe", "hello world");
        var sha256 = ComputeSha256(filePath);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "app.exe", Sha256 = sha256 }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_ValidSignature_MismatchedFileHash_ReturnsFailureWithINT002()
    {
        // Arrange: sign with one hash, but file on disk has different content
        CreateFile("app.exe", "tampered content");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "app.exe", Sha256 = "0000000000000000000000000000000000000000000000000000000000000000" }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message);
        Assert.Contains("app.exe", result.Error.Message);
    }

    [Fact]
    public void Verify_InvalidSignatureBytes_ReturnsFailureWithINT001()
    {
        // Arrange: create a valid-looking envelope but with a bad signature
        var filePath = CreateFile("app.exe", "hello");
        var sha256 = ComputeSha256(filePath);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "app.exe", Sha256 = sha256 }
        };

        // Sign with the real key, then substitute a different key's public key
        var filesJson = JsonSerializer.Serialize(files, IntegritySignatureContext.Default.IReadOnlyListManifestFileEntry);
        var filesBytes = Encoding.UTF8.GetBytes(filesJson);
        var hash = SHA256.HashData(filesBytes);
        var signature = key.SignHash(hash);

        // Use a different key's public key to cause verification failure
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var envelope = new ManifestSignatureEnvelope
        {
            Version = 1,
            Algorithm = "ECDSA-P256",
            PublicKey = Convert.ToBase64String(wrongKey.ExportSubjectPublicKeyInfo()),
            Files = files,
            Signature = Convert.ToBase64String(signature)
        };

        var signatureJson = JsonSerializer.Serialize(envelope, IntegritySignatureContext.Default.ManifestSignatureEnvelope);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT001", result.Error.Message);
    }

    [Fact]
    public void Verify_EmptyFilesList_ValidSignature_ReturnsSuccess()
    {
        // Arrange
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>();
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_FileNotFoundOnDisk_ReturnsFailureWithINT002()
    {
        // Arrange: sign a file that doesn't exist on disk
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "missing.exe", Sha256 = "ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890" }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT002", result.Error.Message);
        Assert.Contains("missing.exe", result.Error.Message);
    }

    [Fact]
    public void Verify_MalformedJson_ReturnsFailureWithINT003()
    {
        // Arrange
        var manifest = CreateManifest("{ this is not valid json }}}");

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT003", result.Error.Message);
    }

    [Fact]
    public void Verify_MissingPublicKey_ReturnsFailureWithINT003()
    {
        // Arrange: envelope with null public key
        var envelope = new ManifestSignatureEnvelope
        {
            Version = 1,
            Algorithm = "ECDSA-P256",
            PublicKey = null!,
            Files = [],
            Signature = "AAAA"
        };
        var json = JsonSerializer.Serialize(envelope, IntegritySignatureContext.Default.ManifestSignatureEnvelope);
        var manifest = CreateManifest(json);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT003", result.Error.Message);
    }

    [Fact]
    public void Verify_MultipleFiles_AllValid_ReturnsSuccess()
    {
        // Arrange
        var file1 = CreateFile("payload/app.exe", "application binary");
        var file2 = CreateFile("payload/config.json", "{\"key\":\"value\"}");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "payload/app.exe", Sha256 = ComputeSha256(file1) },
            new() { Name = "payload/config.json", Sha256 = ComputeSha256(file2) }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Verify_MultipleFiles_SecondFileTampered_ReturnsFailureForTamperedFile()
    {
        // Arrange
        var file1 = CreateFile("payload/app.exe", "application binary");
        CreateFile("payload/config.json", "tampered content");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "payload/app.exe", Sha256 = ComputeSha256(file1) },
            new() { Name = "payload/config.json", Sha256 = "0000000000000000000000000000000000000000000000000000000000000000" }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("config.json", result.Error.Message);
    }

    [Fact]
    public void Verify_PathTraversalInFileName_ReturnsFailureWithINT003()
    {
        // Arrange: file name contains path traversal — must be rejected
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var files = new List<ManifestFileEntry>
        {
            new() { Name = "../../../etc/passwd", Sha256 = "ABCDEF" }
        };
        var signatureJson = CreateSignedEnvelope(key, files);
        var manifest = CreateManifest(signatureJson);

        // Act
        var result = IntegrityVerifier.Verify(manifest, _tempDir);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.IntegrityError, result.Error.Kind);
        Assert.Contains("INT003", result.Error.Message);
    }
}
