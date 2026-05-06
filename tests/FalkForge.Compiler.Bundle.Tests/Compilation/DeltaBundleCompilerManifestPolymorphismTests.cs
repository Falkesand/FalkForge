using System.Text;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// Regression tests for the failure observed in demo 53-delta-updates: the v1 bundle's manifest
/// chain contains <see cref="PackageManifestChainItem"/> (and may contain
/// <see cref="RollbackBoundaryManifestChainItem"/>) — both concrete subclasses of the abstract
/// <see cref="ManifestChainItem"/>. <see cref="DeltaBundleCompiler"/> deserializes that manifest
/// to recover the base version, which fails with NotSupportedException unless the JSON contract
/// declares polymorphism for the abstract base.
/// </summary>
public sealed class DeltaBundleCompilerManifestPolymorphismTests : IDisposable
{
    private readonly string _tempDir;

    public DeltaBundleCompilerManifestPolymorphismTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DeltaPolyTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Compile_OldBundleManifestWithPackageAndRollbackBoundaryChain_DeserializesSuccessfully()
    {
        var oldPayloadData = Encoding.UTF8.GetBytes("v1 payload");
        var oldPayloadPath = Path.Combine(_tempDir, "old_payload.bin");
        File.WriteAllBytes(oldPayloadPath, oldPayloadData);

        var package = new BundlePackageModel
        {
            Id = "MyApp",
            SourcePath = oldPayloadPath,
            Type = BundlePackageType.MsiPackage,
            DisplayName = "My App",
            Vital = true
        };

        var boundary = new RollbackBoundaryModel { Id = "Boundary1", Vital = true };

        var v1Model = new BundleModel
        {
            Name = "DeltaPolyDemo",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new[] { package },
            Chain = new ChainItem[]
            {
                new RollbackBoundaryChainItem(boundary),
                new PackageChainItem(package)
            }
        };

        var v1OutputDir = Path.Combine(_tempDir, "v1");
        var v1Result = new BundleCompiler().Compile(v1Model, v1OutputDir);
        Assert.True(v1Result.IsSuccess,
            $"v1 compile failed: {(v1Result.IsFailure ? v1Result.Error.Message : "")}");

        var newPayloadData = Encoding.UTF8.GetBytes("v2 payload differs slightly");
        var newPayloadPath = Path.Combine(_tempDir, "new_payload.bin");
        File.WriteAllBytes(newPayloadPath, newPayloadData);

        var v2Package = new BundlePackageModel
        {
            Id = "MyApp",
            SourcePath = newPayloadPath,
            Type = BundlePackageType.MsiPackage,
            DisplayName = "My App",
            Vital = true
        };

        var v2Model = new BundleModel
        {
            Name = "DeltaPolyDemo",
            Manufacturer = "TestCo",
            Version = "2.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = v1Model.UpgradeCode,
            Scope = InstallScope.PerMachine,
            Packages = new[] { v2Package },
            Chain = new ChainItem[]
            {
                new RollbackBoundaryChainItem(boundary),
                new PackageChainItem(v2Package)
            }
        };

        var v2OutputDir = Path.Combine(_tempDir, "v2");
        var deltaResult = new DeltaBundleCompiler().Compile(v2Model, v2OutputDir, v1Result.Value);

        Assert.True(deltaResult.IsSuccess,
            $"Delta compile failed reading v1 manifest with polymorphic chain: " +
            $"{(deltaResult.IsFailure ? deltaResult.Error.Message : "")}");
        Assert.True(File.Exists(deltaResult.Value));
    }
}
