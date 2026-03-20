namespace FalkForge.Engine.Detection;

using FalkForge.Platform;

public sealed class MsiDetector
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly IRegistry _registry;

    public MsiDetector(IRegistry registry)
    {
        _registry = registry;
    }

    public bool IsProductInstalled(string productCode)
    {
        var uninstallKey = $@"{UninstallKeyPath}\{productCode}";
        return _registry.KeyExists(RegistryRoot.LocalMachine, uninstallKey)
            || _registry.KeyExists(RegistryRoot.CurrentUser, uninstallKey);
    }

    public string? GetInstalledVersion(string productCode)
    {
        var uninstallKey = $@"{UninstallKeyPath}\{productCode}";
        return _registry.GetStringValue(RegistryRoot.LocalMachine, uninstallKey, "DisplayVersion")
            ?? _registry.GetStringValue(RegistryRoot.CurrentUser, uninstallKey, "DisplayVersion");
    }
}
