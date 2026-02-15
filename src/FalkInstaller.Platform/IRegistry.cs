namespace FalkInstaller.Platform;

public interface IRegistry
{
    bool KeyExists(string rootKey, string subKey);
    string? GetStringValue(string rootKey, string subKey, string valueName);
    int? GetDWordValue(string rootKey, string subKey, string valueName);
    IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey);
}
