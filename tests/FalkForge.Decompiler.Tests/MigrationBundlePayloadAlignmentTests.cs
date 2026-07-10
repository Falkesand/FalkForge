using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// End-to-end alignment invariant for "forge migrate" on a native FalkForge bundle.
///
/// WHY this matters:
/// The migrated Program.cs references each chained package by a payload-relative path
/// (e.g. c.MsiPackage("payload/MyApp.msi", ...)) and the migration also extracts that
/// package's bytes into MigrationResult.Payloads keyed by the same string. If the two
/// keys diverge, the migrated project either references a payload that was never written
/// or writes a payload the code never adds - a silently broken migration. This test
/// compiles a REAL native bundle, runs the REAL migration generator (which opens the
/// bundle, reads the TOC, and extracts payload bytes), and asserts every extracted
/// payload key appears verbatim as a package path in the generated Program.cs.
/// Alignment holds by construction: both sides derive their key from one resolver
/// (BundlePayloadPath).
/// </summary>
public sealed class MigrationBundlePayloadAlignmentTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationBundlePayloadAlignmentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk-migrate-bundle-align-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Generate_RealNativeBundle_PayloadKeysAppearAsPackagePathsInProgramCs()
    {
        // Arrange: a real native bundle with one chained MSI-shaped payload file.
        var payloadPath = Path.Combine(_tempDir, "MyApp.msi");
        File.WriteAllBytes(payloadPath, [0x01, 0x02, 0x03, 0x04, 0x05]);

        var model = new BundleModel
        {
            Name = "AlignBundle",
            Manufacturer = "Align Corp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "MyApp",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp",
                    SourcePath = payloadPath,
                }
            ],
            Chain =
            [
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "MyApp",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp",
                    SourcePath = payloadPath,
                })
            ]
        };

        var outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outDir);
        var compileResult = new BundleCompiler { AllowPlaceholderStub = true }.Compile(model, outDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : "");
        var bundlePath = compileResult.Value;

        // Act: run the REAL generator (opens the bundle, reads TOC, extracts payload bytes).
        var options = new MigrationOptions(FalkForgeSourcePath: "../../src", ProjectName: "AlignedBundle");
        var result = new MigrationProjectGenerator().Generate(bundlePath, options);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

        var migration = result.Value;
        var programCs = migration.TextFiles["Program.cs"];

        // Assert: payloads were extracted, and every key aligns with a package path.
        Assert.NotEmpty(migration.Payloads);
        foreach (var key in migration.Payloads.Keys)
            Assert.Contains($"\"{key}\"", programCs);
    }
}
