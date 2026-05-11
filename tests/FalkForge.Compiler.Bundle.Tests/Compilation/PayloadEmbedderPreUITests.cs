using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class PayloadEmbedderPreUITests : IDisposable
{
    private readonly string _tempDir;
    private readonly PayloadEmbedder _embedder = new();

    public PayloadEmbedderPreUITests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PreUIEmbedTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void PayloadEmbedder_EmbedsPreUIPayloads()
    {
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "bundle.exe");

        var regularData = Encoding.UTF8.GetBytes("Regular package payload");
        var preUIData = Encoding.UTF8.GetBytes("Pre-UI prerequisite payload");
        var (regularPath, regularHash) = CreatePayloadFile(regularData);
        var (preUIPath, preUIHash) = CreatePayloadFile(preUIData);

        var manifest = CreateManifest();
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "RegularPkg", SourcePath = regularPath, OriginalSize = regularData.Length, Sha256Hash = regularHash },
            new PayloadEntry { PackageId = "PreUI_DotNet10", SourcePath = preUIPath, OriginalSize = preUIData.Length, Sha256Hash = preUIHash, IsPreUI = true }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess, embedResult.IsFailure ? embedResult.Error.Message : "");

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess, extractResult.IsFailure ? extractResult.Error.Message : "");

        var content = extractResult.Value;
        Assert.Equal(2, content.TocEntries.Length);

        var regularEntry = content.TocEntries.Single(e => e.PackageId == "RegularPkg");
        var preUIEntry = content.TocEntries.Single(e => e.PackageId == "PreUI_DotNet10");

        Assert.False(regularEntry.IsPreUI);
        Assert.True(preUIEntry.IsPreUI);
        Assert.Equal(preUIHash, preUIEntry.Sha256Hash);
    }

    [Fact]
    public void PayloadEmbedder_RemotePreUIPayload_NotEmbedded()
    {
        // Remote pre-UI payloads (PayloadMode = Remote) must NOT be embedded in the TOC;
        // they are downloaded at install time via PreUIPayloadResolver.
        var stubPath = CreateStub();
        var outputPath = Path.Combine(_tempDir, "bundle_remote.exe");

        var regularData = Encoding.UTF8.GetBytes("Regular package payload");
        var (regularPath, regularHash) = CreatePayloadFile(regularData);

        var manifest = CreateManifest();
        // Only one payload (regular); remote pre-UI is absent from the payload list
        var payloads = new[]
        {
            new PayloadEntry { PackageId = "RegularPkg", SourcePath = regularPath, OriginalSize = regularData.Length, Sha256Hash = regularHash }
        };

        var embedResult = _embedder.Embed(stubPath, outputPath, manifest, payloads);
        Assert.True(embedResult.IsSuccess, embedResult.IsFailure ? embedResult.Error.Message : "");

        var extractResult = PayloadEmbedder.Extract(outputPath);
        Assert.True(extractResult.IsSuccess);
        Assert.Single(extractResult.Value.TocEntries);
        Assert.False(extractResult.Value.TocEntries[0].IsPreUI);
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
