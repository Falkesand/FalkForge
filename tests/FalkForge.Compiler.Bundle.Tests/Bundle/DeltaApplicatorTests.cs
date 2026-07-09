using System.Linq;
using System.Security.Cryptography;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Bundle;

/// <summary>
/// End-to-end tests for the install-time half of delta updates: building a v1 full bundle and a
/// v2 delta bundle with the real compilers, then driving <see cref="DeltaApplicator"/> — the
/// engine-side reconstruction path — to prove a delta payload is reconstructed to the exact v2
/// bytes (and rejected loudly, writing no output, whenever the basis is wrong/missing or the
/// reconstructed content does not match its declared hash).
///
/// Before this type existed, the runtime extraction path (BundleReader) gzip-decompressed a delta
/// payload and verified only the delta-blob hash, writing the raw Octodiff delta blob to the
/// destination as if it were the finished payload — silent garbage. These tests encode that the
/// finished, reconstructed payload (verified against <see cref="TocEntry.ReconstructedSha256Hash"/>)
/// is what lands on disk.
/// </summary>
public sealed class DeltaApplicatorTests : IDisposable
{
    private readonly string _tempDir;

    public DeltaApplicatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DeltaApply_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ReconstructPayloadToFile_ValidDelta_ProducesByteExactNewPayload()
    {
        var (basisBundle, deltaBundle, _, newData, deltaEntry) = BuildV1AndDelta();

        var destDir = Path.Combine(_tempDir, "out_ok");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, basisBundle, destDir, deltaEntry.PackageId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.True(File.Exists(result.Value), "Reconstructed payload file should exist");
        Assert.Equal(newData, File.ReadAllBytes(result.Value));
        Assert.Equal(deltaEntry.ReconstructedSha256Hash,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.Value))), ignoreCase: true);
    }

    [Fact]
    public void ReconstructPayloadToFile_BasisBundleMissingPackage_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A basis bundle that does not contain the delta entry's package at all.
        var unrelated = BuildFullBundle("unrelated", RandomBytes(4_000, seed: 99), "SomeOtherPkg");

        var destDir = Path.Combine(_tempDir, "out_missing");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, unrelated, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis payload is absent");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_WrongBaseVersion_FailsLoud_WritesNoOutput()
    {
        var (_, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // A basis bundle with the SAME package id but DIFFERENT bytes: its payload hash will not
        // match the delta's declared BaseSha256Hash, so it is the wrong base version.
        var wrongBase = BuildFullBundle("wrongbase", RandomBytes(10_000, seed: 123), deltaEntry.PackageId);

        var destDir = Path.Combine(_tempDir, "out_wrongbase");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, deltaEntry, wrongBase, destDir, deltaEntry.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the basis is the wrong base version");
        AssertNoFileWritten(destDir);
    }

    [Fact]
    public void ReconstructPayloadToFile_TamperedReconstructedHash_FailsLoud_WritesNoOutput()
    {
        var (basisBundle, deltaBundle, _, _, deltaEntry) = BuildV1AndDelta();

        // Same delta + correct basis, but the entry's declared reconstructed hash is corrupted.
        // Reconstruction produces the correct bytes, so the ONLY gate that can catch this is the
        // final ReconstructedSha256Hash check — this proves that gate is enforced.
        var tampered = CloneWithReconstructedHash(deltaEntry,
            "0000000000000000000000000000000000000000000000000000000000000000");

        var destDir = Path.Combine(_tempDir, "out_tampered");
        var result = DeltaApplicator.ReconstructPayloadToFile(
            deltaBundle, tampered, basisBundle, destDir, tampered.PackageId);

        Assert.True(result.IsFailure, "Reconstruction must fail loudly when the reconstructed hash does not match");
        AssertNoFileWritten(destDir);
    }

    /// <summary>
    /// Builds a v1 full bundle and a v2 delta bundle (against v1) for a single package, returning
    /// the basis (v1) bundle path, the delta bundle path, the v1 bytes, the v2 bytes, and the delta
    /// TOC entry from the delta bundle.
    /// </summary>
    private (string BasisBundle, string DeltaBundle, byte[] OldData, byte[] NewData, TocEntry DeltaEntry)
        BuildV1AndDelta()
    {
        const string packageId = "AppPkg";

        var oldData = new byte[20_000];
        Array.Fill(oldData, (byte)'X');
        // Sprinkle structure so the delta is meaningfully smaller than a full re-embed.
        for (var i = 0; i < oldData.Length; i += 64)
            oldData[i] = (byte)(i % 251);

        var basisBundle = BuildFullBundle("v1", oldData, packageId);

        var newData = (byte[])oldData.Clone();
        for (var i = 0; i < 40; i++)
            newData[i * 97 % newData.Length] ^= 0xFF;
        var newPayloadPath = WritePayload(newData, "v2_payload.bin");

        var model = CreateModel("DeltaApp", packageId, newPayloadPath);
        var outputDir = Path.Combine(_tempDir, "v2_delta");
        var deltaResult = new DeltaBundleCompiler().Compile(model, outputDir, basisBundle);
        Assert.True(deltaResult.IsSuccess, deltaResult.IsFailure ? deltaResult.Error.Message : "");

        var extract = BundleReader.Extract(deltaResult.Value);
        Assert.True(extract.IsSuccess, extract.IsFailure ? extract.Error.Message : "");
        var deltaEntry = extract.Value.TocEntries.Single(e => e.PackageId == packageId);
        Assert.True(deltaEntry.IsDelta, "Test setup requires the entry to actually be a delta");

        return (basisBundle, deltaResult.Value, oldData, newData, deltaEntry);
    }

    private string BuildFullBundle(string name, byte[] payloadData, string packageId)
    {
        var payloadPath = WritePayload(payloadData, $"{name}_payload.bin");
        var model = CreateModel(name, packageId, payloadPath);
        var outputDir = Path.Combine(_tempDir, $"{name}_bundle");
        var result = new BundleCompiler().Compile(model, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    private string WritePayload(byte[] data, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] RandomBytes(int count, int seed)
    {
        var data = new byte[count];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static void AssertNoFileWritten(string destDir)
    {
        if (!Directory.Exists(destDir))
            return;
        Assert.Empty(Directory.GetFiles(destDir, "*", SearchOption.AllDirectories));
    }

    private static TocEntry CloneWithReconstructedHash(TocEntry entry, string reconstructedHash) => new()
    {
        PackageId = entry.PackageId,
        Offset = entry.Offset,
        CompressedSize = entry.CompressedSize,
        OriginalSize = entry.OriginalSize,
        Sha256Hash = entry.Sha256Hash,
        IsDelta = entry.IsDelta,
        BaseSha256Hash = entry.BaseSha256Hash,
        ReconstructedSha256Hash = reconstructedHash,
        IsPreUI = entry.IsPreUI
    };

    private static BundleModel CreateModel(string name, string packageId, string sourcePath)
    {
        var package = new BundlePackageModel
        {
            Id = packageId,
            SourcePath = sourcePath,
            Type = BundlePackageType.MsiPackage,
            DisplayName = packageId
        };

        return new BundleModel
        {
            Name = name,
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new[] { package }.AsReadOnly()
        };
    }
}
