namespace FalkForge.Plugins.Sql;

using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

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
                        {
                            servers.Add(string.IsNullOrEmpty(instanceName)
                                ? serverName
                                : $@"{serverName}\{instanceName}");
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            return Result<IReadOnlyList<string>>.Success(servers.Order().ToList());
        }, ct);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
                {
                    servers.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                        ? machineName
                        : $@"{machineName}\{name}");
                }
            }
        }
        catch { }
    }
}
