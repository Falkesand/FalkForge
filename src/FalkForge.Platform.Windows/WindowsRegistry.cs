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
