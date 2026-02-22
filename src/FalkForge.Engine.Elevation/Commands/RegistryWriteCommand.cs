namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
public sealed partial class RegistryWriteCommand : IElevatedCommand
{
    // System-reserved application name segments that must never be written to.
    // These are checked case-insensitively against the second path segment.
    private static readonly string[] ReservedAppNames =
    [
        "Microsoft",
        "Classes",
        "Policies",
        "WOW6432Node",
        "RegisteredApplications",
        "Clients",
    ];

    public string Name => "RegistryWrite";

    public Result<byte[]> Execute(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var rootKey = reader.ReadString();
        var subKey = reader.ReadString();
        var valueName = reader.ReadString();
        var valueType = reader.ReadString();
        var valueData = reader.ReadString();

        if (!subKey.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, @"Registry subkey must start with 'SOFTWARE\'");

        // Allowlist step 1: Structural validation — require at least 2 levels deep
        // under SOFTWARE\, where the application name segment contains only safe characters.
        if (!AllowedSubKeyPattern().IsMatch(subKey))
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                @"Registry subkey must match 'SOFTWARE\<AppName>\...' where AppName starts with a letter or digit and contains only alphanumeric, space, dot, underscore, or dash characters");

        // Allowlist step 2: Block system-reserved application name segments.
        // Extract the app name (second segment between first and second backslash).
        var afterSoftware = subKey.AsSpan()[@"SOFTWARE\".Length..];
        var nextSlash = afterSoftware.IndexOf('\\');
        var appName = nextSlash >= 0 ? afterSoftware[..nextSlash] : afterSoftware;

        foreach (var reserved in ReservedAppNames)
        {
            if (appName.Equals(reserved, StringComparison.OrdinalIgnoreCase))
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    $"Registry writes under 'SOFTWARE\\{reserved}' are not allowed");
        }

        try
        {
            var hive = rootKey switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                _ => (RegistryKey?)null
            };

            if (hive is null)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"Unsupported root key: {rootKey}");

            using var key = hive.CreateSubKey(subKey, writable: true);
            if (key is null)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"Failed to create registry key: {rootKey}\\{subKey}");

            switch (valueType)
            {
                case "REG_SZ":
                    key.SetValue(valueName, valueData, RegistryValueKind.String);
                    break;
                case "REG_DWORD":
                    if (!int.TryParse(valueData, out var dwordValue))
                        return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"Invalid DWORD value: {valueData}");
                    key.SetValue(valueName, dwordValue, RegistryValueKind.DWord);
                    break;
                case "REG_EXPAND_SZ":
                    key.SetValue(valueName, valueData, RegistryValueKind.ExpandString);
                    break;
                default:
                    return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"Unsupported value type: {valueType}");
            }

            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<byte[]>.Failure(ErrorKind.ElevationError, $"Access denied: {ex.Message}");
        }
    }

    /// <summary>
    /// Matches SOFTWARE\AppName\... where AppName starts with a letter or digit
    /// and contains only alphanumeric characters, spaces, dots, underscores, or dashes.
    /// Must be at least 2 levels deep (SOFTWARE\AppName\something).
    /// Case-insensitive.
    /// </summary>
    [GeneratedRegex(@"^SOFTWARE\\[A-Za-z0-9][A-Za-z0-9 ._-]*\\", RegexOptions.IgnoreCase)]
    private static partial Regex AllowedSubKeyPattern();
}
