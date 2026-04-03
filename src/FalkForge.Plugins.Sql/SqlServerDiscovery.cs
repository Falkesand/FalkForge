using System.Data;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace FalkForge.Plugins.Sql;

internal sealed class SqlServerDiscovery : ISqlServerDiscovery
{
    private readonly Func<CancellationToken, IEnumerable<string>> _registrySource;
    private readonly Func<CancellationToken, IEnumerable<string>> _networkSource;

    public SqlServerDiscovery()
    {
        _registrySource = OperatingSystem.IsWindows() ? DiscoverFromRegistry : _ => [];
        _networkSource = DiscoverFromNetwork;
    }

    internal SqlServerDiscovery(
        Func<CancellationToken, IEnumerable<string>> registrySource,
        Func<CancellationToken, IEnumerable<string>> networkSource)
    {
        _registrySource = registrySource;
        _networkSource = networkSource;
    }

    public Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var server in _registrySource(ct))
            {
                if (!string.IsNullOrEmpty(server))
                    servers.Add(server);
            }

            ct.ThrowIfCancellationRequested();

            try
            {
                foreach (var server in _networkSource(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(server))
                        servers.Add(server);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or SecurityException)
            {
                Trace.TraceWarning($"SQL Server network discovery failed: {ex.Message}");
            }

            return Result<IReadOnlyList<string>>.Success(servers.Order().ToList());
        }, ct);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> DiscoverFromRegistry(CancellationToken ct)
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

    private static IEnumerable<string> DiscoverFromNetwork(CancellationToken ct)
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