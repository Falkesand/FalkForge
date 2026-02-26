using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class SbomIntegrationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public SbomIntegrationTests() => Directory.CreateDirectory(_tempDir);

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
}
