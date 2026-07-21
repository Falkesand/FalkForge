using System.Text.Json;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class SbomIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public SbomIntegrationTests()
    {
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", null);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void WriteSbomSidecar_WithPackageAndFiles_CreatesSidecarFile()
    {
        // Arrange: use a temporary output path
        var msiOutputPath = Path.Combine(_tempDir, "TestApp.msi");
        var sbomPath = msiOutputPath + ".cdx.json";
        var package = new PackageBuilder
        {
            Name = "TestApp",
            Version = new Version(1, 0, 0),
            Manufacturer = "Contoso"
        }.Sbom().Build();

        // Act: write sidecar directly via SbomHelper
        var result = SbomHelper.WriteSbomSidecar(package, [], msiOutputPath);

        // Assert
        Assert.True(result.IsSuccess, $"Expected success but got: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.True(File.Exists(sbomPath), $"Expected sidecar at {sbomPath}");

        var content = File.ReadAllText(sbomPath);
        Assert.Contains("CycloneDX", content);
        Assert.Contains("TestApp", content);
    }

    [Fact]
    public void WriteSbomSidecar_WhenSbomOptionsNull_ReturnsSuccess_NoFile()
    {
        var msiOutputPath = Path.Combine(_tempDir, "NoSbomApp.msi");
        var sbomPath = msiOutputPath + ".cdx.json";
        var package = new PackageBuilder
        {
            Name = "NoSbomApp",
            Version = new Version(1, 0, 0),
            Manufacturer = "Contoso"
            // No .Sbom() call — SbomOptions is null
        }.Build();

        var result = SbomHelper.WriteSbomSidecar(package, [], msiOutputPath);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(sbomPath), "Sidecar should not be written when SbomOptions is null");
    }

    /// <summary>
    /// <see cref="Builders.PackageBuilder.Reproducible"/> with an explicit epoch override must
    /// reach the SBOM sidecar's identity (serial number + timestamp), not just the deterministic
    /// PackageCode/ProductCode GUIDs. <see cref="SbomHelper.WriteSbomSidecar"/> previously called
    /// <see cref="ReproducibleSbomIdentity.Resolve"/> without threading
    /// <see cref="PackageModel.ReproducibleOptions"/> through, so an explicit override passed in
    /// code (with no env var set) was silently dropped and the SBOM fell back to
    /// Guid.NewGuid()/UtcNow, breaking byte-identical rebuilds. This test asserts precedence
    /// without touching the SOURCE_DATE_EPOCH env var at all — a process-global mutation is
    /// unsafe to rely on for test isolation — which also proves the override wins regardless of
    /// ambient env state.
    /// </summary>
    [Fact]
    public void WriteSbomSidecar_WithReproducibleEpoch_IdentityIsDeterministicAcrossBuilds()
    {
        var package = new PackageBuilder
        {
            Name = "ReproApp",
            Version = new Version(1, 0, 0),
            Manufacturer = "Contoso"
        }.Reproducible(1_700_000_000L).Sbom().Build();

        var msiOutputPath1 = Path.Combine(_tempDir, "out-repro-1.msi");
        var msiOutputPath2 = Path.Combine(_tempDir, "out-repro-2.msi");

        var result1 = SbomHelper.WriteSbomSidecar(package, [], msiOutputPath1);
        var result2 = SbomHelper.WriteSbomSidecar(package, [], msiOutputPath2);

        Assert.True(result1.IsSuccess, result1.IsFailure ? result1.Error.Message : null);
        Assert.True(result2.IsSuccess, result2.IsFailure ? result2.Error.Message : null);

        using var doc1 = JsonDocument.Parse(File.ReadAllText(msiOutputPath1 + ".cdx.json"));
        using var doc2 = JsonDocument.Parse(File.ReadAllText(msiOutputPath2 + ".cdx.json"));

        var serial1 = doc1.RootElement.GetProperty("serialNumber").GetString();
        var serial2 = doc2.RootElement.GetProperty("serialNumber").GetString();
        Assert.Equal(serial1, serial2);

        var timestamp1 = doc1.RootElement.GetProperty("metadata").GetProperty("timestamp").GetString();
        Assert.Equal("2023-11-14T22:13:20Z", timestamp1);
    }
}
