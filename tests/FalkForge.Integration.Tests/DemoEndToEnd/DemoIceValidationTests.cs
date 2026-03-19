using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Validation;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "ICE")]
public sealed class DemoIceValidationTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoIceValidationTests(DemoBuildFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(DemoTestCatalog.MsiDemosData), MemberType = typeof(DemoTestCatalog))]
    public void Msi_PassesIceValidation(DemoExpectation demo)
    {
        if (demo.RequiresInfrastructure) return;

        var build = _fixture.GetOrBuild(demo);
        if (!build.Succeeded) return;

        var validator = new IceValidator();
        var result = validator.Validate(build.OutputFile!);

        // IceValidator returns success with empty results if darice.cub not found
        if (result.IsFailure) return;

        var validation = result.Value;
        Assert.True(validation.IsValid,
            $"ICE validation failed for '{demo.Name}':\n" +
            string.Join("\n", validation.Errors.Concat(validation.Failures)
                .Select(m => $"  {m.IceName}: {m.Description}")));
    }
}
