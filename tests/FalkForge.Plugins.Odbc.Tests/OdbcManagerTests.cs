namespace FalkForge.Plugins.Odbc.Tests;

using FalkForge.Plugins;
using Xunit;

public sealed class OdbcManagerTests
{
    [Fact]
    public void DsnExists_empty_name_returns_failure()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void DsnExists_nonexistent_dsn_returns_false()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("NONEXISTENT_DSN_TEST_12345");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Theory]
    [InlineData(@"..\..\..\Windows\System32\config\SAM")]
    [InlineData("test;DROP TABLE")]
    [InlineData("test/path")]
    [InlineData("test\\path")]
    [InlineData("test\0name")]
    [InlineData("test%00name")]
    public void DsnExists_invalid_characters_returns_validation_failure(string dsnName)
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists(dsnName);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.Contains("invalid characters", result.Error.Message);
    }

    [Theory]
    [InlineData("MyDSN")]
    [InlineData("My DSN Name")]
    [InlineData("DSN-With-Hyphens")]
    [InlineData("DSN_With_Underscores")]
    [InlineData("DSN 123")]
    public void DsnExists_valid_names_pass_validation(string dsnName)
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists(dsnName);
        // Should not fail with validation error — may succeed or fail with PluginError
        if (result.IsFailure)
            Assert.NotEqual(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Plugin_registers_IOdbcManager()
    {
        var registry = new PluginServiceRegistry();
        var plugin = new OdbcPlugin();
        plugin.RegisterServices(registry);

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IOdbcManager>());
    }
}
