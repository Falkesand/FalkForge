using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// ServiceInstall-table reconstruction: maps raw <see cref="ServiceRow"/> entries
/// into <see cref="ServiceModel"/>, translating the MSI start-type/account
/// conventions back into their <see cref="ServiceStartMode"/> / <see cref="ServiceAccount"/>
/// equivalents.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<ServiceModel> BuildServices(IReadOnlyList<ServiceRow> serviceRows)
    {
        return serviceRows
            .Select(r =>
            {
                var startType = r.StartType;
                var startMode = MapStartMode(startType);
                var (account, userName) = MapServiceAccount(r.StartName);
                var deps = r.Dependencies is not null
                    ? r.Dependencies.Split("[~]", StringSplitOptions.RemoveEmptyEntries)
                          .Select(d => d.Trim())
                          .Where(d => !string.IsNullOrEmpty(d))
                          .ToList()
                    : (List<string>)[];
                return new ServiceModel
                {
                    Name = r.Name,
                    DisplayName = r.DisplayName ?? r.Name,
                    Executable = r.ServiceInstall,
                    Description = r.Description,
                    StartMode = startMode,
                    Account = account,
                    UserName = userName,
                    Dependencies = deps
                };
            })
            .ToList();
    }

    private static ServiceStartMode MapStartMode(int msiStartType) => msiStartType switch
    {
        0 => ServiceStartMode.Automatic, // SERVICE_BOOT_START mapped to Automatic
        1 => ServiceStartMode.Automatic, // SERVICE_SYSTEM_START mapped to Automatic
        2 => ServiceStartMode.Automatic, // SERVICE_AUTO_START
        3 => ServiceStartMode.Manual,    // SERVICE_DEMAND_START
        4 => ServiceStartMode.Disabled,  // SERVICE_DISABLED
        _ => ServiceStartMode.Automatic
    };

    private static (ServiceAccount Account, string? UserName) MapServiceAccount(string? startName)
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
}
