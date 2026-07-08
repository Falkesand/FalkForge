using System.Diagnostics;
using System.Text.RegularExpressions;
using FalkForge.Diagnostics;
using Microsoft.Win32;

namespace FalkForge.Plugins.Odbc;

internal sealed partial class OdbcManager : IOdbcManager
{
    private const string Category = "OdbcManager";

    private static readonly Regex ValidDsnNamePattern = GetValidDsnNameRegex();

    private readonly IFalkLogger? _logger;

    public OdbcManager(IFalkLogger? logger = null) => _logger = logger;

    public Result<bool> DsnExists(string dsnName)
    {
        if (string.IsNullOrWhiteSpace(dsnName))
        {
            _logger?.Warning(Category, "DSN lookup rejected: name is empty");
            return Result<bool>.Failure(new Error(ErrorKind.Validation, "DSN name cannot be empty."));
        }

        if (!ValidDsnNamePattern.IsMatch(dsnName))
        {
            _logger?.Warning(Category, $"DSN lookup rejected: '{dsnName}' contains invalid characters");
            return Result<bool>.Failure(new Error(ErrorKind.Validation,
                "DSN name contains invalid characters. Only alphanumeric characters, spaces, hyphens, and underscores are allowed."));
        }

        _logger?.Debug(Category, $"Checking DSN existence: '{dsnName}'");
        try
        {
            var exists = CheckRegistry(@"SOFTWARE\ODBC\ODBC.INI", dsnName)
                         || CheckRegistry(@"SOFTWARE\WOW6432Node\ODBC\ODBC.INI", dsnName);
            _logger?.Info(Category, $"DSN '{dsnName}' exists: {exists}");
            return Result<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            _logger?.Log(LogLevel.Error, Category, $"Failed to check DSN '{dsnName}'", ex,
                new Dictionary<string, string> { ["code"] = nameof(ErrorKind.PluginError) });
            return Result<bool>.Failure(new Error(ErrorKind.PluginError, $"Failed to check DSN: {ex.Message}"));
        }
    }

    public void LaunchOdbcAdministrator()
    {
        var path = Path.Combine(Environment.SystemDirectory, "odbcad32.exe");
        _logger?.Info(Category, $"Launching ODBC Administrator: '{path}'");
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