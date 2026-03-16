namespace FalkForge.Plugins.Sql;

public interface IConnectionTester
{
    Task<Result<Unit>> TestConnectionAsync(
        string server,
        string database,
        bool integratedSecurity,
        string? userName = null,
        string? password = null,
        bool trustServerCertificate = false,
        CancellationToken ct = default);
}