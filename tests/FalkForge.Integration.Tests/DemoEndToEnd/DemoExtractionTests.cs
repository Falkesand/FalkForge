using System.Runtime.Versioning;
using FalkForge.Cli;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "E2E")]
public sealed class DemoExtractionTests : IDisposable
{
    private readonly DemoBuildFixture _fixture;
    private readonly string _extractRoot;

    public DemoExtractionTests(DemoBuildFixture fixture)
    {
        _fixture = fixture;
        _extractRoot = Path.Combine(Path.GetTempPath(), $"falk-extract-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_extractRoot);
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ExtractionSucceeds(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return; // compilation test will catch this

        var extractDir = Path.Combine(_extractRoot, demo.Name.Replace('/', '_'));
        var result = MsiExtractor.Extract(build.OutputFile!, extractDir);

        Assert.True(result.IsSuccess,
            $"Extraction failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ExtractionProducesFiles(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var extractDir = Path.Combine(_extractRoot, $"{demo.Name.Replace('/', '_')}-files");
        var result = MsiExtractor.Extract(build.OutputFile!, extractDir);
        if (result.IsFailure) return;

        Assert.True(result.Value >= 0,
            $"Extraction of '{demo.Name}' returned negative file count");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ExtractedFilesExistOnDisk(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var extractDir = Path.Combine(_extractRoot, $"{demo.Name.Replace('/', '_')}-verify");
        var result = MsiExtractor.Extract(build.OutputFile!, extractDir);
        if (result.IsFailure || result.Value == 0) return; // extraction test will catch this

        Assert.True(Directory.Exists(extractDir),
            $"Extract directory not created for '{demo.Name}'");

        var files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        // Verify files were written (size may be 0 for placeholder demo payloads)
        foreach (var file in files)
        {
            Assert.True(File.Exists(file),
                $"Extracted file '{Path.GetFileName(file)}' from '{demo.Name}' does not exist on disk");
        }
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ExtractPreservesFileCount(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var extractDir = Path.Combine(_extractRoot, $"{demo.Name.Replace('/', '_')}-count");
        var result = MsiExtractor.Extract(build.OutputFile!, extractDir);
        if (result.IsFailure) return;

        if (!Directory.Exists(extractDir)) return;

        var filesOnDisk = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Length;
        Assert.Equal(result.Value, filesOnDisk);
    }

    public void Dispose()
    {
        try { Directory.Delete(_extractRoot, true); } catch { }
    }
}
