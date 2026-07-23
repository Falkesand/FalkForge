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
    [InlineData("dotnet8_found")] // lowercase — legal MSI identifier grammar, but not a PUBLIC property
    public void Validate_IllegalVariableName_ReturnsNET005(string variableName)
    {
        // A LaunchCondition built from an illegal property name (e.g. "1 OR 1") is evaluated by
        // msiexec as an expression, not a property reference — "1 OR 1" is ALWAYS true, so the
        // .NET runtime gate would silently pass even without the runtime installed. This must be
        // rejected at author time, not discovered by a machine missing the runtime. A lowercase
        // name is legal MSI identifier grammar but is a PRIVATE property per the Windows Installer
        // SDK — AppSearch.Property must be PUBLIC (no lowercase letters) to be reliably populated
        // by the built-in AppSearch standard action, so it must be rejected too.
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

    [Fact]
    public void Validate_UndefinedRuntimeType_ReturnsNET004()
    {
        // An out-of-range enum cast (e.g. from a deserialized/reflected value) must not reach the
        // planner — DotNetSearchPlanner.SharedFrameworkInfo's default arm is a defensive throw, not
        // a Result-style failure, so this must be caught here first.
        var model = CreateModel(runtimeType: (DotNetRuntimeType)999);

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("NET004", result.Error.Message);
    }

    [Fact]
    public void Validate_UndefinedPlatform_ReturnsNET007()
    {
        // An out-of-range Platform enum cast must not silently fall through to the 64-bit
        // Program Files root (DotNetSearchPlanner.ProgramFilesRoot's `!= X86` default) — that would
        // search the wrong tree for an unrecognized platform value without any error.
        var model = CreateModel(platform: (DotNetPlatform)999);

        var result = DotNetSearchValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("NET007", result.Error.Message);
    }

    private static DotNetCoreSearchModel CreateModel(
        DotNetRuntimeType runtimeType = DotNetRuntimeType.Runtime,
        DotNetPlatform platform = DotNetPlatform.X64,
        Version? minimumVersion = null,
        string variableName = "DOTNET_RUNTIME_X64",
        bool forceNullVersion = false) => new()
    {
        RuntimeType = runtimeType,
        Platform = platform,
        MinimumVersion = forceNullVersion ? null! : (minimumVersion ?? new Version(8, 0)),
        VariableName = variableName
    };
}
