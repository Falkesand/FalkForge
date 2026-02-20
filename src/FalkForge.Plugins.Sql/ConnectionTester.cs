namespace FalkForge.Plugins.Sql;

using Microsoft.Data.SqlClient;

internal sealed class ConnectionTester : IConnectionTester
{
    public async Task<Result<Unit>> TestConnectionAsync(
        string server, string database, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, database, integratedSecurity, userName, password);
        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(ct);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch (SqlException ex)
        {
            return Result<Unit>.Failure(
                new Error(ErrorKind.PluginError, $"Connection failed: {ex.Message}"));
        }
    }
}
