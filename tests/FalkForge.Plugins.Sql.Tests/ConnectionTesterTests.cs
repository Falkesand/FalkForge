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

    [Fact]
    public async Task TestConnectionAsync_cancelled_token_throws()
    {
        // Kills the L19 statement mutation (removes `throw` in catch OperationCanceledException).
        // When the mutant removes `throw`, the method returns Failure instead of throwing,
        // causing this assertion to fail.
        var tester = new ConnectionTester();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tester.TestConnectionAsync("localhost", "master", true, ct: cts.Token));
    }
}
