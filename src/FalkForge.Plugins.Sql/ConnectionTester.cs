using System.Data.Common;
using FalkForge.Diagnostics;
using Microsoft.Data.SqlClient;

namespace FalkForge.Plugins.Sql;

internal sealed class ConnectionTester : IConnectionTester
{
    private const string Category = "ConnectionTester";

    private readonly Func<string, CancellationToken, Task> _openAsync;
    private readonly IFalkLogger? _logger;

    public ConnectionTester(IFalkLogger? logger = null)
    {
        _openAsync = DefaultOpenAsync;
        _logger = logger;
    }

    internal ConnectionTester(Func<string, CancellationToken, Task> openAsync, IFalkLogger? logger = null)
    {
        _openAsync = openAsync;
        _logger = logger;
    }

    public async Task<Result<Unit>> TestConnectionAsync(
        string server, string database, bool integratedSecurity,
        string? userName = null, string? password = null,
        bool trustServerCertificate = false,
        CancellationToken ct = default)
    {
        _logger?.Info(Category, $"Testing connection to '{server}/{database}'");
        var connStr = ConnectionStringHelper.Build(server, database, integratedSecurity, userName, password,
            trustServerCertificate: trustServerCertificate);
        try
        {
            await _openAsync(connStr, ct);
            _logger?.Info(Category, $"Connection to '{server}/{database}' succeeded");
            return Result<Unit>.Success(Unit.Value);
        }
        catch (DbException ex)
        {
            _logger?.Log(LogLevel.Error, Category, $"Connection to '{server}/{database}' failed", ex,
                new Dictionary<string, string> { ["code"] = nameof(ErrorKind.PluginError) });
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