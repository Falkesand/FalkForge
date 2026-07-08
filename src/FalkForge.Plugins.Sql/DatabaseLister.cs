using System.Data.Common;
using FalkForge.Diagnostics;
using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class DatabaseLister : IDatabaseLister
{
    private const string Category = "DatabaseLister";

    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _queryDatabases;
    private readonly IFalkLogger? _logger;

    public DatabaseLister(IFalkLogger? logger = null)
    {
        _queryDatabases = DefaultQueryDatabasesAsync;
        _logger = logger;
    }

    internal DatabaseLister(
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> queryDatabases,
        IFalkLogger? logger = null)
    {
        _queryDatabases = queryDatabases;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        _logger?.Info(Category, $"Listing databases on '{server}'");
        var connStr = ConnectionStringHelper.Build(server, null, integratedSecurity, userName, password);
        try
        {
            var databases = await _queryDatabases(connStr, ct);
            _logger?.Info(Category, $"Found {databases.Count} database(s) on '{server}'");
            return Result<IReadOnlyList<string>>.Success(databases);
        }
        catch (DbException ex)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to list databases on '{server}'", ex,
                new Dictionary<string, string> { ["code"] = nameof(ErrorKind.PluginError) });
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