using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class SqlServerDiscoveryTests
{
    [Fact]
    public async Task DiscoverServersAsync_returns_result()
    {
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task DiscoverServersAsync_supports_cancellation()
    {
        var discovery = new SqlServerDiscovery();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverServersAsync(cts.Token));
    }
}
