using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class ConnectionTester : IConnectionTester
{
    private readonly Func<string, CancellationToken, Task> _openAsync;

    public ConnectionTester() => _openAsync = DefaultOpenAsync;

    internal ConnectionTester(Func<string, CancellationToken, Task> openAsync) => _openAsync = openAsync;

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
            await _openAsync(connStr, ct);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (DbException ex)
        {
            return Result<Unit>.Failure(
                new Error(ErrorKind.PluginError, $"Connection failed: {ex.Message}"));
        }
    }

    private static async Task DefaultOpenAsync(string connStr, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connStr);
        await connection.OpenAsync(ct);
    }
}