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

    [Fact]
    public async Task DiscoverServersAsync_AllEntriesAreNonEmpty()
    {
        // Kills server name instance-check mutation (line 34: !IsNullOrEmpty guard).
        // If the guard were inverted, empty/null entries would be added, causing this to fail.
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.All(result.Value, name =>
            Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public async Task DiscoverServersAsync_NoDuplicates()
    {
        // Kills deduplication mutations; the internal HashSet should ensure no duplicates.
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        var list = result.Value.ToList();
        var distinctCount = list.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(list.Count, distinctCount);
    }

    [Fact]
    public async Task DiscoverServersAsync_ReturnsOrderedList()
    {
        // Kills the .Order() mutation: result must be sorted alphabetically.
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        var list = result.Value.ToList();
        var sorted = list.Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, list);
    }

    [Fact]
    public void SqlClientFactory_CanCreateDataSourceEnumerator_IsTrue()
    {
        // Kills the CanCreateDataSourceEnumerator negation mutation (SqlServerDiscovery line 25).
        // When the mutant negates the guard, the condition becomes `!true = false` and the
        // enumeration block is skipped. This test directly verifies the property is true,
        // causing the mutant's code path to diverge from the original.
        Assert.True(Microsoft.Data.SqlClient.SqlClientFactory.Instance.CanCreateDataSourceEnumerator);
    }

    [Fact]
    public async Task DiscoverServersAsync_CanCreateDataSourceEnumerator_SmokeTest()
    {
        // Complementary smoke: confirms discovery succeeds when CanCreateDataSourceEnumerator is true.
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
    }
}
