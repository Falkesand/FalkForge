using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class DatabaseLister : IDatabaseLister
{
    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, null, integratedSecurity, userName, password);
        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(ct);

            var databases = new List<string>();
            await using var cmd = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", connection);
            // Stryker disable all : untestable SQL execution loop — requires live SQL Server connection
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                databases.Add(reader.GetString(0));
            // Stryker restore

            return Result<IReadOnlyList<string>>.Success(databases);
        }
        catch (SqlException ex)
        {
            return Result<IReadOnlyList<string>>.Failure(
                new Error(ErrorKind.PluginError, $"Failed to list databases: {ex.Message}"));
        }
    }
}