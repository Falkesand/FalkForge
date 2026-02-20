using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class ConnectionTesterTests
{
    [Fact]
    public async Task TestConnectionAsync_invalid_server_returns_failure()
    {
        var tester = new ConnectionTester();
        var result = await tester.TestConnectionAsync(
            "NONEXISTENT_SERVER_12345", "master", true);
        Assert.True(result.IsFailure);
        Assert.Contains("Connection failed", result.Error.Message);
    }
}
