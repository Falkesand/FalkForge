using FalkInstaller.Extensions.DotNet;
using Xunit;

namespace FalkInstaller.Extensions.DotNet.Tests;

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
