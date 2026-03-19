using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class DatabaseListerTests
{
    [Fact]
    public async Task ListDatabasesAsync_invalid_server_returns_failure()
    {
        // Covers the SqlException catch path (L27-L31 in DatabaseLister.cs).
        // Kills the L30 string mutation ("Failed to list databases" → "").
        var lister = new DatabaseLister();
        var result = await lister.ListDatabasesAsync(
            "NONEXISTENT_SERVER_12345", integratedSecurity: true);

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to list databases", result.Error.Message);
    }

    [Fact]
    public async Task ListDatabasesAsync_cancelled_token_throws()
    {
        // Kills the L26 statement mutation (removes `throw` in catch OperationCanceledException).
        // When the mutant removes `throw`, the method returns Failure instead of throwing.
        var lister = new DatabaseLister();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => lister.ListDatabasesAsync("localhost", integratedSecurity: true, ct: cts.Token));
    }
}
