using FalkInstaller.Models;

namespace FalkInstaller.Decompiler.TableReaders;

/// <summary>
/// Reads the ServiceInstall table from an MSI database.
/// Columns: ServiceInstall, Name, DisplayName, ServiceType, StartType, ErrorControl, LoadOrderGroup, Dependencies, StartName, Password, Arguments, Component_, Description_
/// </summary>
public static class ServiceTableReader
{
    private static readonly string[] Columns = ["ServiceInstall", "Name", "DisplayName", "ServiceType", "StartType", "ErrorControl", "LoadOrderGroup", "Dependencies", "StartName", "Password", "Arguments", "Component_", "Description_"];

    public static Result<List<ServiceModel>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("ServiceInstall");
        if (existsResult.IsFailure)
            return Result<List<ServiceModel>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<ServiceModel>>.Success([]);

        var rowsResult = tableAccess.QueryTable("ServiceInstall", Columns);
        if (rowsResult.IsFailure)
            return Result<List<ServiceModel>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read ServiceInstall table. {rowsResult.Error.Message}");

        var services = new List<ServiceModel>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[4], out var startType);
            var startMode = MapStartMode(startType);
            var (account, userName) = MapServiceAccount(row[8]);
            var dependencies = ParseDependencies(row[7]);

            services.Add(new ServiceModel
            {
                Name = row[1] ?? string.Empty,
                DisplayName = row[2] ?? row[1] ?? string.Empty,
                Executable = row[0] ?? string.Empty, // ServiceInstall key often maps to the component's file
                Description = row[12],
                StartMode = startMode,
                Account = account,
                UserName = userName,
                Dependencies = dependencies
            });
        }

        return services;
    }

    internal static ServiceStartMode MapStartMode(int msiStartType) => msiStartType switch
    {
        0 => ServiceStartMode.Automatic, // SERVICE_BOOT_START mapped to Automatic
        1 => ServiceStartMode.Automatic, // SERVICE_SYSTEM_START mapped to Automatic
        2 => ServiceStartMode.Automatic, // SERVICE_AUTO_START
        3 => ServiceStartMode.Manual,    // SERVICE_DEMAND_START
        4 => ServiceStartMode.Disabled,  // SERVICE_DISABLED
        _ => ServiceStartMode.Automatic
    };

    internal static (ServiceAccount Account, string? UserName) MapServiceAccount(string? startName)
    {
        if (string.IsNullOrEmpty(startName) || startName.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.LocalSystem, null);
        if (startName.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase) ||
            startName.Equals("LocalService", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.LocalService, null);
        if (startName.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase) ||
            startName.Equals("NetworkService", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.NetworkService, null);

        return (ServiceAccount.User, startName);
    }

    private static List<string> ParseDependencies(string? dependencies)
    {
        if (string.IsNullOrEmpty(dependencies))
            return [];

        // MSI dependencies are null-separated in a single string, represented as [~] separator
        return dependencies
            .Split("[~]", StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .Where(d => !string.IsNullOrEmpty(d))
            .ToList();
    }
}
