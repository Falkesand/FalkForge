using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class DatabaseLister : IDatabaseLister
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _queryDatabases;

    public DatabaseLister() => _queryDatabases = DefaultQueryDatabasesAsync;

    internal DatabaseLister(Func<string, CancellationToken, Task<IReadOnlyList<string>>> queryDatabases) =>
        _queryDatabases = queryDatabases;

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, null, integratedSecurity, userName, password);
        try
        {
            var databases = await _queryDatabases(connStr, ct);
            return Result<IReadOnlyList<string>>.Success(databases);
        }
        catch (DbException ex)
        {
            return Result<IReadOnlyList<string>>.Failure(
                new Error(ErrorKind.PluginError, $"Failed to list databases: {ex.Message}"));
        }
    }

    private static async Task<IReadOnlyList<string>> DefaultQueryDatabasesAsync(
        string connStr, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connStr);
        await connection.OpenAsync(ct);

        var databases = new List<string>();
        await using var cmd = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString(0));

        return databases;
    }
}