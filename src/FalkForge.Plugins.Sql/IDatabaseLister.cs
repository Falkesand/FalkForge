namespace FalkForge.Plugins.Sql;

public interface IDatabaseLister
{
    Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server,
        bool integratedSecurity,
        string? userName = null,
        string? password = null,
        CancellationToken ct = default);
}