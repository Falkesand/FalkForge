using System.Runtime.Versioning;
using FalkForge.Cli;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "E2E")]
public sealed class DemoInspectionTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoInspectionTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_HasValidMetadata(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var result = MsiInspector.Inspect(build.OutputFile!);

        Assert.True(result.IsSuccess,
            $"Inspection failed for '{demo.Name}': {(result.IsFailure ? result.Error.Message : "")}");

        var info = result.Value;
        Assert.False(string.IsNullOrWhiteSpace(info.ProductName),
            $"Demo '{demo.Name}' MSI has no ProductName");
        Assert.False(string.IsNullOrWhiteSpace(info.Manufacturer),
            $"Demo '{demo.Name}' MSI has no Manufacturer");
        Assert.False(string.IsNullOrWhiteSpace(info.Version),
            $"Demo '{demo.Name}' MSI has no ProductVersion");
        Assert.False(string.IsNullOrWhiteSpace(info.ProductCode),
            $"Demo '{demo.Name}' MSI has no ProductCode");
        Assert.True(info.TableCount > 0,
            $"Demo '{demo.Name}' MSI has no tables");
    }

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_ContainsRequiredTables(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        if (demo.RequiresInfrastructure || demo.RequiredTables.Length == 0) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var result = MsiInspector.Inspect(build.OutputFile!);
        if (result.IsFailure) return;

        var tableNames = result.Value.TableNames;
        foreach (var expected in demo.RequiredTables)
        {
            Assert.Contains(expected, tableNames,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
