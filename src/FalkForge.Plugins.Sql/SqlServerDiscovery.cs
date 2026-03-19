using System.Data;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace FalkForge.Plugins.Sql;

internal sealed class SqlServerDiscovery : ISqlServerDiscovery
{
    public Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (OperatingSystem.IsWindows())
                DiscoverFromRegistry(servers);

            ct.ThrowIfCancellationRequested();

            try
            {
                var factory = SqlClientFactory.Instance;
                if (factory.CanCreateDataSourceEnumerator)
                {
                    var enumerator = factory.CreateDataSourceEnumerator()!;
                    var table = enumerator.GetDataSources();
                    foreach (DataRow row in table.Rows)
                    {
                        ct.ThrowIfCancellationRequested();
                        var serverName = row["ServerName"]?.ToString();
                        var instanceName = row["InstanceName"]?.ToString();
                        if (!string.IsNullOrEmpty(serverName))
                            // Stryker disable all : untestable DataRow field access — network-dependent external data
                            servers.Add(string.IsNullOrEmpty(instanceName)
                                ? serverName
                                : $@"{serverName}\{instanceName}");
                        // Stryker restore
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // Stryker disable all : untestable catch branch — requires live network to trigger SqlException/InvalidOperationException
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or SecurityException)
            {
                Trace.TraceWarning($"SQL Server network discovery failed: {ex.Message}");
            }
            // Stryker restore

            return Result<IReadOnlyList<string>>.Success(servers.Order().ToList());
        }, ct);
    }

    [SupportedOSPlatform("windows")]
    private static void DiscoverFromRegistry(HashSet<string> servers)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            if (key is not null)
            {
                var machineName = Environment.MachineName;
                foreach (var name in key.GetValueNames())
                    servers.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                        ? machineName
                        : $@"{machineName}\{name}");
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            Trace.TraceWarning($"SQL Server registry discovery failed: {ex.Message}");
        }
    }
}