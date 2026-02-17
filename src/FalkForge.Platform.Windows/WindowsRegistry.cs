using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FalkForge.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    public bool KeyExists(string rootKey, string subKey)
    {
        using var key = GetRootKey(rootKey)?.OpenSubKey(subKey);
        return key is not null;
    }

    public string? GetStringValue(string rootKey, string subKey, string valueName)
    {
        using var key = GetRootKey(rootKey)?.OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    public int? GetDWordValue(string rootKey, string subKey, string valueName)
    {
        using var key = GetRootKey(rootKey)?.OpenSubKey(subKey);
        return key?.GetValue(valueName) as int?;
    }

    public IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey)
    {
        using var key = GetRootKey(rootKey)?.OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? [];
    }

    public void SetStringValue(string rootKey, string subKey, string valueName, string value)
    {
        var root = GetRootKey(rootKey);
        if (root is null) return;

        using var key = root.CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteKey(string rootKey, string subKey)
    {
        var root = GetRootKey(rootKey);
        if (root is null) return;

        try
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch (UnauthorizedAccessException)
        {
            // Silently ignore permission failures during cleanup
        }
    }

    private static RegistryKey? GetRootKey(string rootKey) => rootKey switch
    {
        "HKLM" or "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
        "HKCU" or "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
        "HKCR" or "HKEY_CLASSES_ROOT" => Microsoft.Win32.Registry.ClassesRoot,
        "HKU" or "HKEY_USERS" => Microsoft.Win32.Registry.Users,
        _ => null
    };
}
