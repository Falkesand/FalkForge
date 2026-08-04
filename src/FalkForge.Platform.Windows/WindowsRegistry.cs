using System.Runtime.Versioning;
using FalkForge;
using Microsoft.Win32;

namespace FalkForge.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public bool KeyExists(RegistryRoot rootKey, string subKey)
    {
        using var key = GetRootKey(rootKey).OpenSubKey(subKey);
        return key is not null;
    }

    public string? GetStringValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        using var key = GetRootKey(rootKey).OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    public int? GetDWordValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        using var key = GetRootKey(rootKey).OpenSubKey(subKey);
        return key?.GetValue(valueName) as int?;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot rootKey, string subKey)
    {
        using var key = GetRootKey(rootKey).OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? [];
    }

    public Result<IReadOnlyList<string>> TryReadSubKeyNames(RegistryRoot rootKey, string subKey)
    {
        try
        {
            using var key = GetRootKey(rootKey).OpenSubKey(subKey);
            IReadOnlyList<string> names = key?.GetSubKeyNames() ?? [];
            return Result<IReadOnlyList<string>>.Success(names);
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
            return Result<IReadOnlyList<string>>.Failure(ErrorKind.Validation,
                $"Invalid registry key name under '{rootKey}\\{subKey}': {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Result<IReadOnlyList<string>>.Failure(ErrorKind.SecurityError,
                $"Failed to read registry subkeys under '{rootKey}\\{subKey}': {ex.Message}");
        }
    }

    public Result<string?> TryGetStringValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        try
        {
            using var key = GetRootKey(rootKey).OpenSubKey(subKey);
            var value = key?.GetValue(valueName) as string;
            return Result<string?>.Success(value);
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
            return Result<string?>.Failure(ErrorKind.Validation,
                $"Invalid registry key name under '{rootKey}\\{subKey}': {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Result<string?>.Failure(ErrorKind.SecurityError,
                $"Failed to read registry value '{valueName}' under '{rootKey}\\{subKey}': {ex.Message}");
        }
    }

    public Result<bool> TryValueExists(RegistryRoot rootKey, string subKey, string valueName)
    {
        try
        {
            using var key = GetRootKey(rootKey).OpenSubKey(subKey);
            var exists = key is not null && Array.Exists(key.GetValueNames(),
                name => string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase));
            return Result<bool>.Success(exists);
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
            return Result<bool>.Failure(ErrorKind.Validation,
                $"Invalid registry key name under '{rootKey}\\{subKey}': {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Result<bool>.Failure(ErrorKind.SecurityError,
                $"Failed to read registry value '{valueName}' under '{rootKey}\\{subKey}': {ex.Message}");
        }
    }

    public Result<bool> TryKeyExists(RegistryRoot rootKey, string subKey)
    {
        try
        {
            using var key = GetRootKey(rootKey).OpenSubKey(subKey);
            return Result<bool>.Success(key is not null);
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
            return Result<bool>.Failure(ErrorKind.Validation,
                $"Invalid registry key name under '{rootKey}\\{subKey}': {ex.Message}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return Result<bool>.Failure(ErrorKind.SecurityError,
                $"Failed to read registry key '{rootKey}\\{subKey}': {ex.Message}");
        }
    }

    public void SetStringValue(RegistryRoot rootKey, string subKey, string valueName, string value)
    {
        using var key = GetRootKey(rootKey).CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteKey(RegistryRoot rootKey, string subKey)
    {
        try
        {
            GetRootKey(rootKey).DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Silently ignore permission failures during cleanup
        }
    }

    private static RegistryKey GetRootKey(RegistryRoot rootKey) => rootKey switch
    {
        RegistryRoot.LocalMachine => Microsoft.Win32.Registry.LocalMachine,
        RegistryRoot.CurrentUser => Microsoft.Win32.Registry.CurrentUser,
        RegistryRoot.ClassesRoot => Microsoft.Win32.Registry.ClassesRoot,
        RegistryRoot.Users => Microsoft.Win32.Registry.Users,
        _ => throw new ArgumentOutOfRangeException(nameof(rootKey), rootKey, null)
    };
}
