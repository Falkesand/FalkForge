using FalkForge.Extensions.DotNet;
using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetSearchValidatorTests
{
    [Fact]
    public void Validate_MissingVariableName_ReturnsNET001()
    {
        var model = CreateModel(variableName: "");

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("NET001", result.Error.Message);
    }

    [Fact]
    public void Validate_NullMinimumVersion_ReturnsNET002()
    {
        var model = CreateModel(forceNullVersion: true);

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("NET002", result.Error.Message);
    }

    [Fact]
    public void Validate_ValidModel_ReturnsSuccess()
    {
        var model = CreateModel();

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("8")] // leading digit — not a legal MSI identifier
    [InlineData("1 OR 1")] // operators + spaces — would make the LaunchCondition always-true
    [InlineData("NET 8")] // embedded space breaks the MSI property/condition grammar
    public void Validate_IllegalVariableName_ReturnsNET005(string variableName)
    {
        // A LaunchCondition built from an illegal property name (e.g. "1 OR 1") is evaluated by
        // msiexec as an expression, not a property reference — "1 OR 1" is ALWAYS true, so the
        // .NET runtime gate would silently pass even without the runtime installed. This must be
        // rejected at author time, not discovered by a machine missing the runtime.
        var model = CreateModel(variableName: variableName);

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("NET005", result.Error.Message);
    }

    [Fact]
    public void Validate_UppercaseVariableName_StillSucceeds()
    {
        var model = CreateModel(variableName: "DOTNET8_FOUND");

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    private static DotNetCoreSearchModel CreateModel(
        DotNetRuntimeType runtimeType = DotNetRuntimeType.Runtime,
        DotNetPlatform platform = DotNetPlatform.X64,
        Version? minimumVersion = null,
        string variableName = "DotNet_Runtime_X64",
        bool forceNullVersion = false) => new()
    {
        RuntimeType = runtimeType,
        Platform = platform,
        MinimumVersion = forceNullVersion ? null! : (minimumVersion ?? new Version(8, 0)),
        VariableName = variableName
    };
}
