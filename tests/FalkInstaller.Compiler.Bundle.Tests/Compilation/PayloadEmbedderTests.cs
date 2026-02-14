using System.Security.Cryptography;
using System.Text;
using FalkInstaller.Compiler.Bundle.Compilation;
using FalkInstaller.Engine.Protocol.Manifest;
using Xunit;

namespace FalkInstaller.Compiler.Bundle.Tests.Compilation;

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
        var hash = Convert.ToHexString(SHA256.HashData(payloadData));

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "TestPkg", Data = payloadData, Sha256Hash = hash }
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
        var hash1 = Convert.ToHexString(SHA256.HashData(data1));
        var hash2 = Convert.ToHexString(SHA256.HashData(data2));
        var hash3 = Convert.ToHexString(SHA256.HashData(data3));

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "Pkg1", Data = data1, Sha256Hash = hash1 },
            new PayloadEntry { PackageId = "Pkg2", Data = data2, Sha256Hash = hash2 },
            new PayloadEntry { PackageId = "Pkg3", Data = data3, Sha256Hash = hash3 }
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
        var hash = Convert.ToHexString(SHA256.HashData(payloadData));

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "CompressTest", Data = payloadData, Sha256Hash = hash }
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
