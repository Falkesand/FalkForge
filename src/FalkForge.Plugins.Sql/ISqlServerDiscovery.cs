namespace FalkForge.Plugins.Sql;

public interface ISqlServerDiscovery
{
    Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default);
}
