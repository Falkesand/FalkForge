using System.Data.Common;
using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class DatabaseListerTests
{
    private sealed class TestDbException(string message) : DbException(message);

    [Fact]
    public async Task ListDatabasesAsync_success_returns_database_list()
    {
        var lister = new DatabaseLister((_, _) =>
            Task.FromResult<IReadOnlyList<string>>(["master", "tempdb", "model"]));

        var result = await lister.ListDatabasesAsync("server", integratedSecurity: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(["master", "tempdb", "model"], result.Value);
    }

    [Fact]
    public async Task ListDatabasesAsync_empty_list_returns_success()
    {
        var lister = new DatabaseLister((_, _) =>
            Task.FromResult<IReadOnlyList<string>>([]));

        var result = await lister.ListDatabasesAsync("server", integratedSecurity: true);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ListDatabasesAsync_db_exception_returns_failure()
    {
        var lister = new DatabaseLister((_, _) =>
            throw new TestDbException("Network error"));

        var result = await lister.ListDatabasesAsync("server", integratedSecurity: true);

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to list databases", result.Error.Message);
        Assert.Contains("Network error", result.Error.Message);
    }

    [Fact]
    public async Task ListDatabasesAsync_db_exception_has_plugin_error_kind()
    {
        var lister = new DatabaseLister((_, _) =>
            throw new TestDbException("fail"));

        var result = await lister.ListDatabasesAsync("server", integratedSecurity: true);

        Assert.Equal(ErrorKind.PluginError, result.Error.Kind);
    }

    [Fact]
    public async Task ListDatabasesAsync_cancelled_token_throws()
    {
        var lister = new DatabaseLister((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>([]);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lister.ListDatabasesAsync("server", integratedSecurity: true, ct: cts.Token));
    }

    [Fact]
    public async Task ListDatabasesAsync_passes_correct_connection_string()
    {
        string? capturedConnStr = null;
        var lister = new DatabaseLister((connStr, _) =>
        {
            capturedConnStr = connStr;
            return Task.FromResult<IReadOnlyList<string>>([]);
        });

        await lister.ListDatabasesAsync("myserver", integratedSecurity: false,
            userName: "sa", password: "pass");

        Assert.NotNull(capturedConnStr);
        Assert.Contains("myserver", capturedConnStr);
        Assert.Contains("sa", capturedConnStr);
    }
}
