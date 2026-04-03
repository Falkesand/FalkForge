using System.Data.Common;
using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class ConnectionTesterTests
{
    private sealed class TestDbException(string message) : DbException(message);

    [Fact]
    public async Task TestConnectionAsync_success_returns_success()
    {
        var tester = new ConnectionTester((_, _) => Task.CompletedTask);

        var result = await tester.TestConnectionAsync("server", "db", true);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TestConnectionAsync_db_exception_returns_failure()
    {
        var tester = new ConnectionTester((_, _) =>
            throw new TestDbException("Connection refused"));

        var result = await tester.TestConnectionAsync("server", "db", true);

        Assert.True(result.IsFailure);
        Assert.Contains("Connection failed", result.Error.Message);
        Assert.Contains("Connection refused", result.Error.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_db_exception_has_plugin_error_kind()
    {
        var tester = new ConnectionTester((_, _) =>
            throw new TestDbException("timeout"));

        var result = await tester.TestConnectionAsync("server", "db", true);

        Assert.Equal(ErrorKind.PluginError, result.Error.Kind);
    }

    [Fact]
    public async Task TestConnectionAsync_cancelled_token_throws()
    {
        var tester = new ConnectionTester((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tester.TestConnectionAsync("server", "db", true, ct: cts.Token));
    }

    [Fact]
    public async Task TestConnectionAsync_passes_correct_connection_string()
    {
        string? capturedConnStr = null;
        var tester = new ConnectionTester((connStr, _) =>
        {
            capturedConnStr = connStr;
            return Task.CompletedTask;
        });

        await tester.TestConnectionAsync("myserver", "mydb", integratedSecurity: false,
            userName: "sa", password: "pass", trustServerCertificate: true);

        Assert.NotNull(capturedConnStr);
        Assert.Contains("myserver", capturedConnStr);
        Assert.Contains("mydb", capturedConnStr);
        Assert.Contains("sa", capturedConnStr);
    }
}
