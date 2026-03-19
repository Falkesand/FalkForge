using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FalkForge.Plugins.Odbc;

internal sealed partial class OdbcManager : IOdbcManager
{
    private static readonly Regex ValidDsnNamePattern = GetValidDsnNameRegex();

    public Result<bool> DsnExists(string dsnName)
    {
        if (string.IsNullOrWhiteSpace(dsnName))
            return Result<bool>.Failure(new Error(ErrorKind.Validation, "DSN name cannot be empty."));

        if (!ValidDsnNamePattern.IsMatch(dsnName))
            return Result<bool>.Failure(new Error(ErrorKind.Validation,
                "DSN name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed."));

        try
        {
            var exists = CheckRegistry(@"SOFTWARE\ODBC\ODBC.INI", dsnName)
                         || CheckRegistry(@"SOFTWARE\WOW6432Node\ODBC\ODBC.INI", dsnName);
            return Result<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new Error(ErrorKind.PluginError, $"Failed to check DSN: {ex.Message}"));
        }
    }

    public void LaunchOdbcAdministrator()
    {
        var path = Path.Combine(Environment.SystemDirectory, "odbcad32.exe");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [GeneratedRegex(@"^[A-Za-z0-9_ \-]+$")]
    private static partial Regex GetValidDsnNameRegex();

    private static bool CheckRegistry(string basePath, string dsnName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{basePath}\{dsnName}");
        if (key is not null) return true;
        using var userKey = Registry.CurrentUser.OpenSubKey($@"{basePath}\{dsnName}");
        return userKey is not null;
    }
}