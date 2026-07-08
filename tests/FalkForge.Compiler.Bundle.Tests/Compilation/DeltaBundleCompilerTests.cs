using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Bundle.Compression;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class DeltaBundleCompilerTests : IDisposable
{
    private readonly string _tempDir;

    public DeltaBundleCompilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DeltaTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Compile_ProducesSmallerBundle()
    {
        // Use a structured payload that has enough variety to not compress trivially
        // with GZip, but has enough repeated blocks for rsync delta to be effective.
        // Simulates a real MSI with repeated table structures and varying data.
        var rng = new Random(42);
        var oldPayloadData = new byte[200_000];
        // Fill with a mix of structured and semi-random data
        for (var i = 0; i < oldPayloadData.Length; i++)
            oldPayloadData[i] = (byte)((i % 256) ^ (i / 1000 % 37));
        // Add some random patches to make it non-trivially compressible
        for (var i = 0; i < 5000; i++)
            oldPayloadData[rng.Next(oldPayloadData.Length)] = (byte)rng.Next(256);

        var oldBundlePath = CreateFullBundle("old", oldPayloadData, "OldPkg");

        // Create new payload with very small differences (~0.1% changed)
        var newPayloadData = (byte[])oldPayloadData.Clone();
        for (var i = 0; i < 200; i++)
            newPayloadData[rng.Next(newPayloadData.Length)] ^= 0xFF;
        var newPayloadPath = CreatePayloadFile(newPayloadData, "new_payload.bin");

        var model = CreateModel("TestBundle", [("OldPkg", newPayloadPath)]);
        var outputDir = Path.Combine(_tempDir, "delta_output");

        var deltaCompiler = new DeltaBundleCompiler();
        var result = deltaCompiler.Compile(model, outputDir, oldBundlePath);

        Assert.True(result.IsSuccess, $"Delta compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(result.Value));

        var fullBundlePath = CreateFullBundleFromModel(model, "full_output");
        var deltaSize = new FileInfo(result.Value).Length;
        var fullSize = new FileInfo(fullBundlePath).Length;

        Assert.True(deltaSize < fullSize,
            $"Delta bundle ({deltaSize} bytes) should be smaller than full bundle ({fullSize} bytes)");
    }

    [Fact]
    public void Compile_DeltaBundle_HasIsDeltaFlag()
    {
        // Create old bundle
        var oldPayloadData = new byte[10_000];
        Array.Fill(oldPayloadData, (byte)'X');
        var oldBundlePath = CreateFullBundle("old_flag", oldPayloadData, "Pkg1");

        // Create slightly modified new payload
        var newPayloadData = (byte[])oldPayloadData.Clone();
        newPayloadData[500] = (byte)'Y';
        var newPayloadPath = CreatePayloadFile(newPayloadData, "new_flag_payload.bin");

        var model = CreateModel("FlagTestBundle", [("Pkg1", newPayloadPath)]);
        var outputDir = Path.Combine(_tempDir, "flag_output");

        var deltaCompiler = new DeltaBundleCompiler();
        var result = deltaCompiler.Compile(model, outputDir, oldBundlePath);

        Assert.True(result.IsSuccess, $"Delta compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        // Read the delta bundle TOC and verify IsDelta flags
        var extractResult = BundleReader.Extract(result.Value);
        Assert.True(extractResult.IsSuccess, $"Extract failed: {(extractResult.IsFailure ? extractResult.Error.Message : "")}");

        var entries = extractResult.Value.TocEntries;
        Assert.Single(entries);
        Assert.True(entries[0].IsDelta, "TOC entry should be marked as delta");
        Assert.NotNull(entries[0].BaseSha256Hash);
        Assert.NotNull(entries[0].ReconstructedSha256Hash);
    }

    [Fact]
    public void Compile_NewPackage_NotInOldBundle_IncludesFullPayload()
    {
        // Create old bundle with one package
        var oldPayloadData = Encoding.UTF8.GetBytes("old package content");
        var oldBundlePath = CreateFullBundle("old_partial", oldPayloadData, "ExistingPkg");

        // New model has a package that doesn't exist in old bundle
        var newPayloadData = Encoding.UTF8.GetBytes("brand new package content");
        var newPayloadPath = CreatePayloadFile(newPayloadData, "brand_new.bin");

        var model = CreateModel("PartialDelta", [("BrandNewPkg", newPayloadPath)]);
        var outputDir = Path.Combine(_tempDir, "partial_output");

        var deltaCompiler = new DeltaBundleCompiler();
        var result = deltaCompiler.Compile(model, outputDir, oldBundlePath);

        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var extractResult = BundleReader.Extract(result.Value);
        Assert.True(extractResult.IsSuccess);

        var entries = extractResult.Value.TocEntries;
        Assert.Single(entries);
        // New package not in old bundle should be full (not delta)
        Assert.False(entries[0].IsDelta, "New package not in old bundle should not be delta");
    }

    /// <summary>
    /// Regression test for the A3 streaming refactor: with more than one delta-eligible payload,
    /// the parallel per-payload old-payload extraction, delta creation, and compression staging
    /// (each now via per-payload temp files rather than in-memory arrays held for the whole
    /// batch) must still produce a correct, independent delta per package — and each delta must
    /// still reconstruct its own new payload byte-exact via BundleReader + DeltaCompressor.
    /// </summary>
    [Fact]
    public void Compile_MultiplePayloads_EachReconstructsByteExactIndependently()
    {
        var rng = new Random(7);
        var oldDataA = new byte[50_000];
        rng.NextBytes(oldDataA);
        var oldDataB = new byte[60_000];
        rng.NextBytes(oldDataB);

        var oldPathA = CreatePayloadFile(oldDataA, "old_a.bin");
        var oldPathB = CreatePayloadFile(oldDataB, "old_b.bin");
        var oldModel = CreateModel("old_multi", [("PkgA", oldPathA), ("PkgB", oldPathB)]);
        var oldBundlePath = CreateFullBundleFromModel(oldModel, "old_multi_bundle");

        // PkgA: small delta-eligible change. PkgB: also a small change. Both should delta.
        var newDataA = (byte[])oldDataA.Clone();
        for (var i = 0; i < 50; i++)
            newDataA[rng.Next(newDataA.Length)] ^= 0xFF;
        var newDataB = (byte[])oldDataB.Clone();
        for (var i = 0; i < 50; i++)
            newDataB[rng.Next(newDataB.Length)] ^= 0xFF;

        var newPathA = CreatePayloadFile(newDataA, "new_a.bin");
        var newPathB = CreatePayloadFile(newDataB, "new_b.bin");
        var model = CreateModel("MultiDelta", [("PkgA", newPathA), ("PkgB", newPathB)]);
        var outputDir = Path.Combine(_tempDir, "multi_delta_output");

        var deltaCompiler = new DeltaBundleCompiler();
        var result = deltaCompiler.Compile(model, outputDir, oldBundlePath);
        Assert.True(result.IsSuccess, $"Delta compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var extractResult = BundleReader.Extract(result.Value);
        Assert.True(extractResult.IsSuccess, extractResult.IsFailure ? extractResult.Error.Message : "");
        var entries = extractResult.Value.TocEntries.ToDictionary(e => e.PackageId);

        Assert.Equal(2, entries.Count);
        Assert.True(entries["PkgA"].IsDelta);
        Assert.True(entries["PkgB"].IsDelta);

        // Reconstruct each delta against its own old-bundle basis and prove it matches its OWN new
        // data byte-exact — proves the per-payload temp-file staging never cross-wires payloads
        // (e.g. writes PkgA's delta/compressed bytes into PkgB's slot) under parallelism.
        AssertDeltaReconstructsTo(oldBundlePath, result.Value, entries["PkgA"], oldDataA, newDataA);
        AssertDeltaReconstructsTo(oldBundlePath, result.Value, entries["PkgB"], oldDataB, newDataB);
    }

    /// <summary>
    /// Extracts a delta TOC entry's raw delta bytes from <paramref name="deltaBundlePath"/> and the
    /// matching old payload from <paramref name="oldBundlePath"/>, applies the delta, and asserts
    /// the reconstructed bytes equal <paramref name="expectedNewData"/> and its hash matches
    /// <see cref="TocEntry.ReconstructedSha256Hash"/>.
    /// </summary>
    private static void AssertDeltaReconstructsTo(
        string oldBundlePath, string deltaBundlePath, TocEntry entry, byte[] oldData, byte[] expectedNewData)
    {
        var oldExtract = BundleReader.Extract(oldBundlePath);
        Assert.True(oldExtract.IsSuccess, oldExtract.IsFailure ? oldExtract.Error.Message : "");
        var oldEntry = oldExtract.Value.TocEntries.Single(e => e.PackageId == entry.PackageId);

        var oldPayload = BundleReader.ExtractPayload(oldBundlePath, oldEntry);
        Assert.True(oldPayload.IsSuccess, oldPayload.IsFailure ? oldPayload.Error.Message : "");
        Assert.Equal(oldData, oldPayload.Value);

        var deltaPayload = BundleReader.ExtractPayload(deltaBundlePath, entry);
        Assert.True(deltaPayload.IsSuccess, deltaPayload.IsFailure ? deltaPayload.Error.Message : "");

        using var basisStream = new MemoryStream(oldPayload.Value);
        using var deltaStream = new MemoryStream(deltaPayload.Value);
        using var outputStream = new MemoryStream();
        var applyResult = DeltaCompressor.ApplyDelta(basisStream, deltaStream, outputStream);
        Assert.True(applyResult.IsSuccess, applyResult.IsFailure ? applyResult.Error.Message : "");

        var reconstructed = outputStream.ToArray();
        Assert.Equal(expectedNewData, reconstructed);
        Assert.Equal(entry.ReconstructedSha256Hash, Convert.ToHexString(SHA256.HashData(reconstructed)),
            ignoreCase: true);
    }

    [Fact]
    public void Compile_OldPayloadCorrupted_FallsBackToFullEmbedForAffectedPackageOnly()
    {
        // Old bundle has two packages. GoodPkg's old payload is intact; CorruptPkg's is
        // tampered with directly on disk after compiling the old bundle, so BundleReader's
        // per-payload SHA-256 check fails when DeltaBundleCompiler reads it back to diff against.
        var goodOldData = new byte[10_000];
        Array.Fill(goodOldData, (byte)'G');
        var corruptOldData = Encoding.UTF8.GetBytes("this old payload will be corrupted on disk before delta compile");

        var goodOldPath = CreatePayloadFile(goodOldData, "good_old.bin");
        var corruptOldPath = CreatePayloadFile(corruptOldData, "corrupt_old.bin");
        var oldModel = CreateModel("old_mixed", [("GoodPkg", goodOldPath), ("CorruptPkg", corruptOldPath)]);
        var oldBundlePath = CreateFullBundleFromModel(oldModel, "old_mixed_bundle");

        CorruptOldPayload(oldBundlePath, "CorruptPkg");

        // New payloads: GoodPkg has a tiny (delta-eligible) change; CorruptPkg is also updated.
        var goodNewData = (byte[])goodOldData.Clone();
        goodNewData[500] = (byte)'H';
        var corruptNewData = Encoding.UTF8.GetBytes(
            "this old payload will be corrupted on disk before delta compile - updated");

        var goodNewPath = CreatePayloadFile(goodNewData, "good_new.bin");
        var corruptNewPath = CreatePayloadFile(corruptNewData, "corrupt_new.bin");

        var newModel = CreateModel("MixedDelta", [("GoodPkg", goodNewPath), ("CorruptPkg", corruptNewPath)]);
        var outputDir = Path.Combine(_tempDir, "mixed_delta_output");

        var deltaCompiler = new DeltaBundleCompiler();
        var result = deltaCompiler.Compile(newModel, outputDir, oldBundlePath);

        Assert.True(result.IsSuccess, $"Delta compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var extractResult = BundleReader.Extract(result.Value);
        Assert.True(extractResult.IsSuccess, extractResult.IsFailure ? extractResult.Error.Message : "");
        var entries = extractResult.Value.TocEntries.ToDictionary(e => e.PackageId);

        Assert.True(entries["GoodPkg"].IsDelta, "Package whose old payload is intact should still use a delta");
        Assert.False(entries["CorruptPkg"].IsDelta,
            "Package whose old payload failed extraction must fall back to a full embed, not abort the whole compile");

        // The fallback full payload must still be usable — extract it and confirm it matches the new content.
        var corruptPkgPayload = BundleReader.ExtractPayload(result.Value, entries["CorruptPkg"]);
        Assert.True(corruptPkgPayload.IsSuccess, corruptPkgPayload.IsFailure ? corruptPkgPayload.Error.Message : "");
        Assert.Equal(corruptNewData, corruptPkgPayload.Value);
    }

    /// <summary>
    /// Flips a few bytes in <paramref name="packageId"/>'s compressed payload region directly on
    /// disk, without touching the TOC or footer, so BundleReader.Extract still lists it correctly
    /// but BundleReader.ExtractPayload fails its SHA-256 check when reading it back.
    /// </summary>
    private static void CorruptOldPayload(string bundlePath, string packageId)
    {
        var extractResult = BundleReader.Extract(bundlePath);
        Assert.True(extractResult.IsSuccess, extractResult.IsFailure ? extractResult.Error.Message : "");
        var entry = extractResult.Value.TocEntries.Single(e => e.PackageId == packageId);

        var bytes = File.ReadAllBytes(bundlePath);
        var start = (int)entry.Offset;
        for (var i = 0; i < 8 && start + i < bytes.Length; i++)
            bytes[start + i] ^= 0xFF;
        File.WriteAllBytes(bundlePath, bytes);
    }

    private string CreateFullBundle(string name, byte[] payloadData, string packageId)
    {
        var payloadPath = CreatePayloadFile(payloadData, $"{name}_payload.bin");
        var model = CreateModel(name, [(packageId, payloadPath)]);
        return CreateFullBundleFromModel(model, $"{name}_bundle");
    }

    private string CreateFullBundleFromModel(BundleModel model, string outputDirName)
    {
        var outputDir = Path.Combine(_tempDir, outputDirName);
        var compiler = new BundleCompiler();
        var result = compiler.Compile(model, outputDir);
        Assert.True(result.IsSuccess, $"Full bundle compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        return result.Value;
    }

    private string CreatePayloadFile(byte[] data, string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static BundleModel CreateModel(string name, (string Id, string SourcePath)[] packages)
    {
        var bundlePackages = packages.Select(p => new BundlePackageModel
        {
            Id = p.Id,
            SourcePath = p.SourcePath,
            Type = BundlePackageType.MsiPackage,
            DisplayName = p.Id
        }).ToList();

        return new BundleModel
        {
            Name = name,
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = bundlePackages.AsReadOnly()
        };
    }
}
