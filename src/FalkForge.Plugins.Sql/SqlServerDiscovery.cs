using System.Data;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using FalkForge.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace FalkForge.Plugins.Sql;

internal sealed class SqlServerDiscovery : ISqlServerDiscovery
{
    private const string Category = "SqlServerDiscovery";

    private readonly Func<CancellationToken, IEnumerable<string>> _registrySource;
    private readonly Func<CancellationToken, IEnumerable<string>> _networkSource;
    private readonly IFalkLogger? _logger;

    public SqlServerDiscovery(IFalkLogger? logger = null)
    {
        _registrySource = OperatingSystem.IsWindows() ? DiscoverFromRegistry : _ => [];
        _networkSource = DiscoverFromNetwork;
        _logger = logger;
    }

    internal SqlServerDiscovery(
        Func<CancellationToken, IEnumerable<string>> registrySource,
        Func<CancellationToken, IEnumerable<string>> networkSource,
        IFalkLogger? logger = null)
    {
        _registrySource = registrySource;
        _networkSource = networkSource;
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            _logger?.Info(Category, "Starting SQL Server discovery (registry + network)");
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var server in _registrySource(ct))
            {
                if (!string.IsNullOrEmpty(server))
                {
                    servers.Add(server);
                    if (_logger is not null && _logger.MinimumLevel <= LogLevel.Debug)
                        _logger.Debug(Category, $"Registry candidate: {server}");
                }
            }

            ct.ThrowIfCancellationRequested();

            try
            {
                foreach (var server in _networkSource(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(server))
                    {
                        servers.Add(server);
                        if (_logger is not null && _logger.MinimumLevel <= LogLevel.Debug)
                            _logger.Debug(Category, $"Network candidate: {server}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or SecurityException)
            {
                Trace.TraceWarning($"SQL Server network discovery failed: {ex.Message}");
                _logger?.Log(LogLevel.Warning, Category, "SQL Server network discovery failed", ex);
            }

            _logger?.Info(Category, $"SQL Server discovery complete: {servers.Count} server(s) found");
            return Result<IReadOnlyList<string>>.Success(servers.Order().ToList());
        }, ct);
    }

    [SupportedOSPlatform("windows")]
    private static List<string> DiscoverFromRegistry(CancellationToken ct)
    {
        var results = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            if (key is not null)
            {
                var machineName = Environment.MachineName;
                foreach (var name in key.GetValueNames())
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                        ? machineName
                        : $@"{machineName}\{name}");
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Trace.TraceWarning($"SQL Server registry discovery failed: {ex.Message}");
        }

        return results;
    }

    private static List<string> DiscoverFromNetwork(CancellationToken ct)
    {
        var results = new List<string>();
        var factory = SqlClientFactory.Instance;
        if (!factory.CanCreateDataSourceEnumerator)
            return results;

        var enumerator = factory.CreateDataSourceEnumerator()!;
        var table = enumerator.GetDataSources();
        foreach (DataRow row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();
            var serverName = row["ServerName"]?.ToString();
            var instanceName = row["InstanceName"]?.ToString();
            if (!string.IsNullOrEmpty(serverName))
                results.Add(string.IsNullOrEmpty(instanceName)
                    ? serverName
                    : $@"{serverName}\{instanceName}");
        }

        return results;
    }
}