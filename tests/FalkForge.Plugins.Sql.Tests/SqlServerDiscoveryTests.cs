using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class SqlServerDiscoveryTests
{
    private static SqlServerDiscovery CreateDiscovery(
        IEnumerable<string>? registryServers = null,
        IEnumerable<string>? networkServers = null) =>
        new(
            registrySource: _ => registryServers ?? [],
            networkSource: _ => networkServers ?? []);

    [Fact]
    public async Task DiscoverServersAsync_returns_success()
    {
        var discovery = CreateDiscovery();

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task DiscoverServersAsync_combines_registry_and_network_sources()
    {
        var discovery = CreateDiscovery(
            registryServers: ["SERVER1"],
            networkServers: ["SERVER2"]);

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains("SERVER1", result.Value);
        Assert.Contains("SERVER2", result.Value);
    }

    [Fact]
    public async Task DiscoverServersAsync_deduplicates_case_insensitive()
    {
        var discovery = CreateDiscovery(
            registryServers: ["MyServer", "ANOTHER"],
            networkServers: ["MYSERVER", "another"]);

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task DiscoverServersAsync_returns_sorted_results()
    {
        var discovery = CreateDiscovery(
            registryServers: ["Zebra", "Alpha"],
            networkServers: ["Middle"]);

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        var list = result.Value.ToList();
        var sorted = list.Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, list);
    }

    [Fact]
    public async Task DiscoverServersAsync_filters_empty_names()
    {
        var discovery = CreateDiscovery(
            registryServers: ["Server1", "", null!],
            networkServers: ["", "Server2"]);

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.All(result.Value, name =>
            Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task DiscoverServersAsync_supports_cancellation()
    {
        var discovery = CreateDiscovery();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverServersAsync(cts.Token));
    }

    [Fact]
    public async Task DiscoverServersAsync_network_exception_is_swallowed()
    {
        var discovery = new SqlServerDiscovery(
            registrySource: _ => ["FromRegistry"],
            networkSource: _ => throw new InvalidOperationException("network down"));

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("FromRegistry", result.Value[0]);
    }

    [Fact]
    public async Task DiscoverServersAsync_empty_sources_returns_empty_list()
    {
        var discovery = CreateDiscovery();

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task DiscoverServersAsync_with_instances_formats_correctly()
    {
        var discovery = CreateDiscovery(
            registryServers: [@"SERVER1\SQLEXPRESS"],
            networkServers: ["SERVER2"]);

        var result = await discovery.DiscoverServersAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains(@"SERVER1\SQLEXPRESS", result.Value);
        Assert.Contains("SERVER2", result.Value);
    }
}
