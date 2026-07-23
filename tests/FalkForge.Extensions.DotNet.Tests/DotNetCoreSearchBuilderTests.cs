using FalkForge.Extensions.DotNet;
using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetCoreSearchBuilderTests
{
    [Fact]
    public void Build_WithAllProperties_ReturnsModelWithCorrectValues()
    {
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.AspNetCore)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Variable("DotNet_AspNetCore_X64")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(DotNetRuntimeType.AspNetCore, model.RuntimeType);
        Assert.Equal(DotNetPlatform.X64, model.Platform);
        Assert.Equal(new Version(8, 0), model.MinimumVersion);
        Assert.Equal("DotNet_AspNetCore_X64", model.VariableName);
    }

    [Fact]
    public void Build_FluentChaining_ReturnsSameBuilder()
    {
        var builder = new DotNetCoreSearchBuilder();
        var returned = builder
            .RuntimeType(DotNetRuntimeType.Runtime)
            .Platform(DotNetPlatform.X86)
            .MinVersion(new Version(6, 0))
            .Variable("DotNet_Runtime_X86");

        Assert.Same(builder, returned);
    }

    [Fact]
    public void Build_MissingVariableName_ReturnsFailureNET001()
    {
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.Runtime)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("NET001", result.Error.Message);
    }

    [Fact]
    public void Build_MissingMinVersion_ReturnsFailureNET002()
    {
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.Runtime)
            .Platform(DotNetPlatform.X64)
            .Variable("DotNet_Runtime_X64")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("NET002", result.Error.Message);
    }

    [Fact]
    public void Build_Sdk_ReturnsNET004()
    {
        // Sdk has no MSI-native shared-framework directory to search (the SDK is versioned via
        // dotnet\sdk\{version}\, a different layout than the sharedfx directories the planner
        // targets), so it is rejected at author time rather than silently producing a plan that
        // can never find anything.
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.Sdk)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Variable("DotNet_Sdk_X64")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("NET004", result.Error.Message);
    }

    [Fact]
    public void Build_WithMessage_SetsMessage()
    {
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.Runtime)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Variable("DOTNET8_FOUND")
            .Message(".NET 8.0 Runtime (x64) or later is required.")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal(".NET 8.0 Runtime (x64) or later is required.", result.Value.Message);
    }

    [Fact]
    public void Build_WithoutMessage_LeavesMessageNull()
    {
        var result = new DotNetCoreSearchBuilder()
            .RuntimeType(DotNetRuntimeType.Runtime)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Variable("DOTNET8_FOUND")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Message);
    }

    [Fact]
    public void SearchForRuntime_ReturnsBuilder_ThatBuildsSuccessfully()
    {
        var builder = new DotNetExtension().SearchForRuntime();

        var result = builder
            .RuntimeType(DotNetRuntimeType.AspNetCore)
            .Platform(DotNetPlatform.X64)
            .MinVersion(new Version(8, 0))
            .Variable("DotNet_AspNetCore_X64")
            .Build();

        Assert.True(result.IsSuccess);
    }
}
