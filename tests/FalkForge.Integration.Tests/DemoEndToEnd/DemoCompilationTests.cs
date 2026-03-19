using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
public sealed class DemoCompilationTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoCompilationTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.AllDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Demo_ProducesOutputFile(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure)
            return;

        var result = _fixture.GetOrBuild(demo);

        Assert.True(result.ExitCode == 0,
            $"Demo '{demo.Name}' failed with exit code {result.ExitCode}.\nStderr: {result.Stderr}");
        var filesInDir = Directory.Exists(result.OutputDir)
            ? string.Join(", ", Directory.GetFiles(result.OutputDir, "*.*", SearchOption.AllDirectories)
                .Select(Path.GetFileName))
            : "(dir not found)";
        Assert.True(result.OutputFile is not null,
            $"Demo '{demo.Name}' produced no output file. Files in output dir: [{filesInDir}]\nStdout: {result.Stdout}");
        Assert.True(File.Exists(result.OutputFile),
            $"Demo '{demo.Name}' output file not found at {result.OutputFile}");
        Assert.True(new FileInfo(result.OutputFile).Length > 0,
            $"Demo '{demo.Name}' produced empty output file");
    }
}
