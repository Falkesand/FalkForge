using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
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
