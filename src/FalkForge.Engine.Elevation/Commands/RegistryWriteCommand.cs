namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.Versioning;
using Microsoft.Win32;

[SupportedOSPlatform("windows")]
public sealed class RegistryWriteCommand : IElevatedCommand
{
    private static readonly string[] DeniedSubKeyPrefixes =
    [
        @"SOFTWARE\Microsoft\",
        @"SOFTWARE\Classes\",
        @"SOFTWARE\Policies\",
        @"SYSTEM\",
        @"SECURITY\",
        @"SAM\"
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

        foreach (var denied in DeniedSubKeyPrefixes)
        {
            if (subKey.StartsWith(denied, StringComparison.OrdinalIgnoreCase))
                return Result<byte[]>.Failure(ErrorKind.SecurityError, $"Registry subkey prefix '{denied.TrimEnd('\\')}' is not allowed");
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
}
