using System.Text.Json;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// The compiler turns each package's declared elevated properties into the signed per-package
/// allowlist, adds ADDLOCAL for packages that enable feature selection (the runtime planner stamps
/// it for those), and omits the field when no package declares anything.
/// </summary>
[Collection("BundleIntegrityEnv")]
public sealed class BundleCompilerPropertyAllowlistTests : IDisposable
{
    private readonly string _tempDir;

    public BundleCompilerPropertyAllowlistTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleAllowlistTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private string CreatePayload(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, $"payload-{fileName}");
        return path;
    }

    private static BundleModel ModelWithPackages(params BundlePackageModel[] packages) => new()
    {
        Name = "SignedBundle",
        Manufacturer = "TestCo",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = packages,
        Integrity = new IntegrityConfiguration()
    };

    private BundlePackageModel Package(string id, string[]? allowed = null, bool featureSelection = false) => new()
    {
        Id = id,
        SourcePath = CreatePayload($"{id}.msi"),
        Type = BundlePackageType.MsiPackage,
        DisplayName = id,
        AllowedElevatedProperties = allowed ?? [],
        EnableFeatureSelection = featureSelection
    };

    private ManifestSignatureEnvelope CompileAndReadEnvelope(BundleModel model, string outName)
    {
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, Path.Combine(_tempDir, outName));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var content = PayloadEmbedder.Extract(result.Value);
        Assert.True(content.IsSuccess);
        var manifest = JsonSerializer.Deserialize(
            content.Value.ManifestJsonBytes!, ManifestJsonContext.Default.InstallerManifest)!;
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope));
        return envelope;
    }

    [Fact]
    public void Compile_AllowedElevatedProperties_SignsSortedAllowlistPerPackage()
    {
        var model = ModelWithPackages(
            Package("PkgA", ["DBSERVER", "DBNAME"]),
            Package("PkgB", ["LICENSEKEY"]));

        var envelope = CompileAndReadEnvelope(model, "out-a");

        Assert.Equal(3, envelope.Version);
        var lists = envelope.PropertyAllowlists!.OrderBy(l => l.PackageId, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, lists.Length);
        Assert.Equal("PkgA", lists[0].PackageId);
        Assert.Equal(["DBNAME", "DBSERVER"], lists[0].PropertyNames);
        Assert.Equal("PkgB", lists[1].PackageId);
        Assert.Equal(["LICENSEKEY"], lists[1].PropertyNames);
    }

    [Fact]
    public void Compile_FeatureSelectionPackage_SignsAddLocalAutomatically()
    {
        var model = ModelWithPackages(Package("PkgA", featureSelection: true));

        var envelope = CompileAndReadEnvelope(model, "out-b");

        var list = Assert.Single(envelope.PropertyAllowlists!);
        Assert.Equal(["ADDLOCAL"], list.PropertyNames);
    }

    [Fact]
    public void Compile_NoAllowedProperties_OmitsPropertyAllowlists()
    {
        var model = ModelWithPackages(Package("PkgA"), Package("PkgB"));

        var envelope = CompileAndReadEnvelope(model, "out-c");

        Assert.Null(envelope.PropertyAllowlists);
    }

    [Fact]
    public void Compile_DuplicateAllowedProperty_SignedOnce()
    {
        var model = ModelWithPackages(Package("PkgA", ["INSTALLDIR", "INSTALLDIR", "ADDLOCAL"], featureSelection: true));

        var envelope = CompileAndReadEnvelope(model, "out-d");

        var list = Assert.Single(envelope.PropertyAllowlists!);
        Assert.Equal(["ADDLOCAL", "INSTALLDIR"], list.PropertyNames);
    }
}
