using FalkForge.Extensions.Util.UserManagement;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.UserManagement;

public sealed class GroupBuilderTests
{
    [Fact]
    public void Build_WithName_ReturnsSuccess()
    {
        var result = new GroupBuilder().Name("Ops").Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("Ops", result.Value.Name);
    }

    [Fact]
    public void Build_WithoutName_ReturnsGRP001()
    {
        var result = new GroupBuilder().Build();

        Assert.True(result.IsFailure);
        Assert.Contains("GRP001", result.Error.Message);
    }

    [Fact]
    public void Build_WithInjectionInName_ReturnsGRP002()
    {
        var result = new GroupBuilder().Name("bad\\group").Build();

        Assert.True(result.IsFailure);
        Assert.Contains("GRP002", result.Error.Message);
    }

    [Fact]
    public void Build_WithFlagsAndDescription_SetsThem()
    {
        var result = new GroupBuilder()
            .Name("Ops")
            .Description("Operations group")
            .UpdateIfExists()
            .RemoveOnUninstall()
            .Build();

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("Operations group", result.Value.Description);
        Assert.True(result.Value.UpdateIfExists);
        Assert.True(result.Value.RemoveOnUninstall);
    }

    [Fact]
    public void Build_FluentChaining_ReturnsSameBuilder()
    {
        var builder = new GroupBuilder();
        var returned = builder.Name("Ops").Description("d").RemoveOnUninstall();
        Assert.Same(builder, returned);
    }
}
