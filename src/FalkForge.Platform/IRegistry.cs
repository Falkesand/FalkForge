using FalkForge;

namespace FalkForge.Platform;

public interface IRegistry
{
    bool KeyExists(RegistryRoot rootKey, string subKey);
    string? GetStringValue(RegistryRoot rootKey, string subKey, string valueName);
    int? GetDWordValue(RegistryRoot rootKey, string subKey, string valueName);
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot rootKey, string subKey);
    void SetStringValue(RegistryRoot rootKey, string subKey, string valueName, string value);
    void DeleteKey(RegistryRoot rootKey, string subKey);
}
