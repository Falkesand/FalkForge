using FalkInstaller.Extensions.Util.QuietExec;
using Xunit;

namespace FalkInstaller.Extensions.Util.Tests.QuietExec;

public sealed class QuietExecBuilderTests
{
    [Fact]
    public void Build_WithRequiredFields_ReturnsSuccess()
    {
        var result = new QuietExecBuilder()
            .Id("exec1")
            .Command("netsh advfirewall set rule")
            .Build();

        Assert.True(result.IsSuccess);
        Assert.Equal("exec1", result.Value.Id);
        Assert.Equal("netsh advfirewall set rule", result.Value.CommandLine);
    }

    [Fact]
    public void Build_WithoutId_ReturnsFailure()
    {
        var result = new QuietExecBuilder()
            .Command("netsh advfirewall set rule")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("QEX001", result.Error.Message);
    }

    [Fact]
    public void Build_WithoutCommand_ReturnsFailure()
    {
        var result = new QuietExecBuilder()
            .Id("exec1")
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("QEX002", result.Error.Message);
    }

    [Fact]
    public void Build_WithAllOptions_SetsAllFields()
    {
        var result = new QuietExecBuilder()
            .Id("exec1")
            .Command("netsh advfirewall add rule")
            .WorkingDir("[INSTALLFOLDER]")
            .Condition("NOT Installed")
            .RollbackCommand("netsh advfirewall delete rule")
            .Build();

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal("[INSTALLFOLDER]", model.WorkingDirectory);
        Assert.Equal("NOT Installed", model.Condition);
        Assert.Equal("netsh advfirewall delete rule", model.RollbackCommandLine);
    }

    [Fact]
    public void Build_FluentChaining_ReturnsSameBuilder()
    {
        var builder = new QuietExecBuilder();

        var returned = builder
            .Id("exec1")
            .Command("cmd")
            .WorkingDir("C:\\Temp")
            .Condition("1")
            .RollbackCommand("cmd /c echo rollback");

        Assert.Same(builder, returned);
    }

    [Fact]
    public void Build_CommandLineExceedsMaxLength_ReturnsQEX003()
    {
        var longCommand = new string('x', 32768);

        var result = new QuietExecBuilder()
            .Id("exec1")
            .Command(longCommand)
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("QEX003", result.Error.Message);
    }

    [Fact]
    public void Build_CommandLineAtMaxLength_ReturnsSuccess()
    {
        var maxCommand = new string('x', 32767);

        var result = new QuietExecBuilder()
            .Id("exec1")
            .Command(maxCommand)
            .Build();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Build_RollbackCommandExceedsMaxLength_ReturnsQEX004()
    {
        var longCommand = new string('x', 32768);

        var result = new QuietExecBuilder()
            .Id("exec1")
            .Command("cmd /c echo ok")
            .RollbackCommand(longCommand)
            .Build();

        Assert.True(result.IsFailure);
        Assert.Contains("QEX004", result.Error.Message);
    }
}
