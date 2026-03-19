using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class ConnectionTester : IConnectionTester
{
    public async Task<Result<Unit>> TestConnectionAsync(
        string server, string database, bool integratedSecurity,
        string? userName = null, string? password = null,
        bool trustServerCertificate = false,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, database, integratedSecurity, userName, password,
            trustServerCertificate: trustServerCertificate);
        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(ct);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (SqlException ex)
        {
            return Result<Unit>.Failure(
                new Error(ErrorKind.PluginError, $"Connection failed: {ex.Message}"));
        }
    }
}