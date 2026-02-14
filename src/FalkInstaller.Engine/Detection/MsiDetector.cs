namespace FalkInstaller.Engine.Detection;

using FalkInstaller.Platform;

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
        return _registry.KeyExists("HKLM", uninstallKey)
            || _registry.KeyExists("HKCU", uninstallKey);
    }

    public string? GetInstalledVersion(string productCode)
    {
        var uninstallKey = $@"{UninstallKeyPath}\{productCode}";
        return _registry.GetStringValue("HKLM", uninstallKey, "DisplayVersion")
            ?? _registry.GetStringValue("HKCU", uninstallKey, "DisplayVersion");
    }
}
