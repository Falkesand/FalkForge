using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class PayloadEmbedderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PayloadEmbedder _embedder = new();

    public PayloadEmbedderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"EmbedTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void EmbedAndExtract_RoundTrip_PreservesTocEntries()
    {
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "bundle.exe");
        var payloadData = Encoding.UTF8.GetBytes("Test payload data for MSI package");
        var (payloadPath, hash) = CreatePayloadFile(payloadData);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "TestPkg", SourcePath = payloadPath, OriginalSize = payloadData.Length, Sha256Hash = hash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess, $"Embed failed: {(embedResult.IsFailure ? embedResult.Error.Message : "")}");

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, $"Extract failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");

        var content = extractResult.Value;
        Assert.Single(content.TocEntries);
        Assert.Equal("TestPkg", content.TocEntries[0].PackageId);
        Assert.Equal(payloadData.Length, content.TocEntries[0].OriginalSize);
        Assert.Equal(hash, content.TocEntries[0].Sha256Hash);
        Assert.Equal(outputPath, content.BundlePath);
    }

    [Fact]
    public void Extract_InvalidBundle_ReturnsFailure()
    {
        var invalidFile = Path.Combine(_tempDir, "invalid.exe");
        File.WriteAllText(invalidFile, "This is not a valid bundle file");

        var result = PayloadEmbedder.Extract(invalidFile);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }

    [Fact]
    public void EmbedAndExtract_MultiplePayloads_PreservesAll()
    {
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "multi.exe");

        var data1 = Encoding.UTF8.GetBytes("First package content");
        var data2 = Encoding.UTF8.GetBytes("Second package content - longer data for testing");
        var data3 = Encoding.UTF8.GetBytes("Third");
        var (path1, hash1) = CreatePayloadFile(data1);
        var (path2, hash2) = CreatePayloadFile(data2);
        var (path3, hash3) = CreatePayloadFile(data3);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "Pkg1", SourcePath = path1, OriginalSize = data1.Length, Sha256Hash = hash1 },
            new PayloadEntry { PackageId = "Pkg2", SourcePath = path2, OriginalSize = data2.Length, Sha256Hash = hash2 },
            new PayloadEntry { PackageId = "Pkg3", SourcePath = path3, OriginalSize = data3.Length, Sha256Hash = hash3 }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess);

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess);

        var entries = extractResult.Value.TocEntries;
        Assert.Equal(3, entries.Length);

        Assert.Equal("Pkg1", entries[0].PackageId);
        Assert.Equal(data1.Length, entries[0].OriginalSize);
        Assert.Equal(hash1, entries[0].Sha256Hash);

        Assert.Equal("Pkg2", entries[1].PackageId);
        Assert.Equal(data2.Length, entries[1].OriginalSize);
        Assert.Equal(hash2, entries[1].Sha256Hash);

        Assert.Equal("Pkg3", entries[2].PackageId);
        Assert.Equal(data3.Length, entries[2].OriginalSize);
        Assert.Equal(hash3, entries[2].Sha256Hash);
    }

    [Fact]
    public void EmbedAndExtract_CompressedSizeRecorded()
    {
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "compressed.exe");
        var payloadData = new byte[10_000];
        Array.Fill(payloadData, (byte)'X');
        var (payloadPath, hash) = CreatePayloadFile(payloadData);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "CompressTest", SourcePath = payloadPath, OriginalSize = payloadData.Length, Sha256Hash = hash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess);

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess);

        var entry = extractResult.Value.TocEntries[0];
        Assert.True(entry.CompressedSize > 0);
        Assert.True(entry.CompressedSize < entry.OriginalSize,
            $"Compressed ({entry.CompressedSize}) should be less than original ({entry.OriginalSize}) for repetitive data");
    }

    [Fact]
    public void BundleMagic_HasCorrectLength()
    {
        Assert.Equal(16, PayloadEmbedder.BundleMagic.Length);
    }

    [Fact]
    public void BundleMagic_StartsWithFalkBundle()
    {
        var magic = PayloadEmbedder.BundleMagic;
        var text = Encoding.ASCII.GetString(magic[..10]);
        Assert.Equal("FALKBUNDLE", text);
    }

    [Fact]
    public void Extract_NonExistentFile_ReturnsFailure()
    {
        var result = PayloadEmbedder.Extract(Path.Combine(_tempDir, "nonexistent.exe"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }

    [Fact]
    public void Extract_TamperedPayload_ReturnsFailure()
    {
        // Arrange: create a valid bundle, extract it to confirm it works, then tamper
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "tampered.exe");
        // Use large repetitive data to ensure GZip compressed output is non-trivial
        var payloadData = new byte[4096];
        Array.Fill(payloadData, (byte)0xAB);
        var (payloadPath, hash) = CreatePayloadFile(payloadData);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "IntegrityPkg", SourcePath = payloadPath, OriginalSize = payloadData.Length, Sha256Hash = hash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess);

        // Extract once to get TOC entry offset, then tamper at that offset
        var validResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(validResult.IsSuccess, "Pre-tamper extraction must succeed");
        var entry = validResult.Value.TocEntries[0];

        // Act: tamper with the compressed payload at the exact offset stored in the TOC
        var bundleBytes = File.ReadAllBytes(outputPath);
        var payloadStart = (int)entry.Offset;
        // Flip bytes in the compressed data region
        for (var i = 0; i < Math.Min(8, entry.CompressedSize); i++)
            bundleBytes[payloadStart + i] ^= 0xFF;
        File.WriteAllBytes(outputPath, bundleBytes);

        // Assert: extraction should detect the integrity failure
        var result = PayloadEmbedder.Extract(outputPath);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
    }

    [Fact]
    public void Extract_ValidPayload_PassesIntegrityCheck()
    {
        // Arrange: create a valid bundle — hash should match
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "valid_integrity.exe");
        var payloadData = Encoding.UTF8.GetBytes("Payload data that should pass integrity check");
        var (payloadPath, hash) = CreatePayloadFile(payloadData);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "ValidPkg", SourcePath = payloadPath, OriginalSize = payloadData.Length, Sha256Hash = hash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess);

        // Act & Assert: extraction should succeed when payloads are intact
        var result = PayloadEmbedder.Extract(outputPath);
        Assert.True(result.IsSuccess, $"Extract should succeed for valid bundle: {(result.IsFailure ? result.Error.Message : "")}");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(100_001)]
    [InlineData(int.MaxValue)]
    public void Extract_InvalidEntryCount_ReturnsFailure(int entryCount)
    {
        // Arrange: craft a minimal bundle with an invalid entryCount in the TOC
        var bundlePath = Path.Combine(_tempDir, $"bad_count_{entryCount}.exe");
        using (var stream = new FileStream(bundlePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // Write stub
            writer.Write(Encoding.UTF8.GetBytes("STUB"));

            // Write magic
            writer.Write(PayloadEmbedder.BundleMagic.ToArray());

            // Write a minimal manifest
            var manifestBytes = Encoding.UTF8.GetBytes("{}");
            writer.Write(manifestBytes.Length);
            writer.Write(manifestBytes);

            // Record TOC offset
            var tocOffset = stream.Position;

            // Write invalid entry count
            writer.Write(entryCount);

            // Write footer (magic + TOC offset)
            writer.Write(PayloadEmbedder.BundleMagic.ToArray());
            writer.Write(tocOffset);
        }

        // Act
        var result = PayloadEmbedder.Extract(bundlePath);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.PayloadError, result.Error.Kind);
        Assert.Contains("entry count", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_ZeroEntryCount_Succeeds()
    {
        // Arrange: bundle with zero entries is valid (empty bundle)
        var bundlePath = Path.Combine(_tempDir, "zero_entries.exe");
        using (var stream = new FileStream(bundlePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.UTF8.GetBytes("STUB"));
            writer.Write(PayloadEmbedder.BundleMagic.ToArray());
            var manifestBytes = Encoding.UTF8.GetBytes("{}");
            writer.Write(manifestBytes.Length);
            writer.Write(manifestBytes);
            var tocOffset = stream.Position;
            writer.Write(0); // zero entries
            writer.Write(PayloadEmbedder.BundleMagic.ToArray());
            writer.Write(tocOffset);
        }

        // Act
        var result = PayloadEmbedder.Extract(bundlePath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.TocEntries);
    }

    private (string Path, string Hash) CreatePayloadFile(byte[] data)
    {
        var path = Path.Combine(_tempDir, $"payload_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, data);
        var hash = Convert.ToHexString(SHA256.HashData(data));
        return (path, hash);
    }

    private string CreateStub()
    {
        var path = Path.Combine(_tempDir, $"stub_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("STUB"));
        return path;
    }

    private static InstallerManifest CreateManifest() => new()
    {
        Name = "Test",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerMachine
    };
}
