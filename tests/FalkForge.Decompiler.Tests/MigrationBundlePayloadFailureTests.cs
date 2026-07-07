using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Fail-loud invariant for "forge migrate" bundle payload extraction (FIX 5).
///
/// WHY this matters:
/// The generated Program.cs references each chained package by its payload-relative path,
/// expecting the migration to have written those bytes. If BundleReader.ExtractPayload fails on
/// a payload in a real bundle and the generator swallows it (returning an empty payload map and
/// a SUCCESS), the migrated project compiles but references payload files that were never
/// written — a silently broken migration. The generator must surface the extraction failure as a
/// Result failure instead, mirroring the MSI path's fail-loud behaviour.
/// </summary>
public sealed class MigrationBundlePayloadFailureTests : IDisposable
{
    private readonly string _tempDir;

    public MigrationBundlePayloadFailureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"falk-migrate-bundle-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Generate_BundlePayloadCorrupted_ReturnsFailureNotEmptySuccess()
    {
        // Arrange: a real native bundle, then corrupt a payload byte so the manifest/TOC
        // still decompile (BundleDecompiler succeeds) but BundleReader.ExtractPayload fails
        // its SHA-256 integrity check when the generator reads the payload back.
        var payloadPath = Path.Combine(_tempDir, "MyApp.msi");
        File.WriteAllBytes(payloadPath, [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80]);

        var model = new BundleModel
        {
            Name = "FailBundle",
            Manufacturer = "Corp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "MyApp", Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp", SourcePath = payloadPath,
                }
            ],
            Chain =
            [
                new PackageChainItem(new BundlePackageModel
                {
                    Id = "MyApp", Type = BundlePackageType.MsiPackage,
                    DisplayName = "MyApp", SourcePath = payloadPath,
                })
            ]
        };

        var outDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outDir);
        var compileResult = new BundleCompiler().Compile(model, outDir);
        Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : "");
        var bundlePath = compileResult.Value;

        CorruptCompressedPayloadRegion(bundlePath);

        // Act
        var options = new MigrationOptions(FalkForgeSourcePath: "../../src", ProjectName: "FailMigrated");
        var result = new MigrationProjectGenerator().Generate(bundlePath, options);

        // Assert: the swallowed-failure bug would yield IsSuccess with an empty payload map.
        Assert.True(result.IsFailure,
            "Corrupted bundle payload extraction must surface as a failure, not an empty success.");
    }

    /// <summary>
    /// Flips bytes in the gzip payload region of a FALKBUNDLE so its SHA-256 integrity check
    /// fails, without touching the footer magic, TOC, or manifest (so decompilation still works).
    /// The payload region sits after the leading stub/magic/manifest and before the TOC.
    /// </summary>
    private static void CorruptCompressedPayloadRegion(string bundlePath)
    {
        var bytes = File.ReadAllBytes(bundlePath);

        // Flip a run of bytes in the middle third of the file. The footer (last 24 bytes) and
        // the very start (PE stub) are left intact; the middle covers the embedded payload.
        var start = bytes.Length / 3;
        var end = Math.Min(bytes.Length - 24, start + 16);
        for (var i = start; i < end; i++)
            bytes[i] ^= 0xFF;

        File.WriteAllBytes(bundlePath, bytes);
    }
}
