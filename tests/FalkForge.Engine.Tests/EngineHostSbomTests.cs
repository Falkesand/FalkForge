namespace FalkForge.Engine.Tests;

using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class EngineHostSbomTests : IDisposable
{
    private readonly string _tempDir;

    public EngineHostSbomTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FalkForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void ExtractSbom_NullSbomAttestation_ReturnsExitCode1()
    {
        var manifest = TestManifestFactory.CreateSimple();

        var exitCode = EngineHost.ExtractSbom(manifest, Path.Combine(_tempDir, "sbom.json"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void ExtractSbom_NullSbomAttestation_DoesNotCreateFile()
    {
        var manifest = TestManifestFactory.CreateSimple();
        var outputPath = Path.Combine(_tempDir, "sbom.json");

        EngineHost.ExtractSbom(manifest, outputPath);

        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void ExtractSbom_WithSbomAttestation_ReturnsExitCode0()
    {
        var manifest = CreateManifestWithSbom("{ \"bomFormat\": \"CycloneDX\" }");
        var outputPath = Path.Combine(_tempDir, "sbom.json");

        var exitCode = EngineHost.ExtractSbom(manifest, outputPath);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ExtractSbom_WithSbomAttestation_WritesContentToFile()
    {
        const string sbomContent = "{ \"bomFormat\": \"CycloneDX\", \"version\": 1 }";
        var manifest = CreateManifestWithSbom(sbomContent);
        var outputPath = Path.Combine(_tempDir, "sbom.json");

        EngineHost.ExtractSbom(manifest, outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Equal(sbomContent, File.ReadAllText(outputPath));
    }

    private static InstallerManifest CreateManifestWithSbom(string sbomContent)
    {
        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [TestManifestFactory.CreateMsiPackage()],
            SbomAttestation = sbomContent
        };
    }
}
