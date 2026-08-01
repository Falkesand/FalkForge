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

    /// <summary>
    /// Reads the direct child subkey names under <paramref name="subKey"/>, reporting a read failure
    /// (access denied / unreadable) instead of silently collapsing it to an empty list. A missing key is
    /// SUCCESS with an empty list — only a genuine read error (e.g. <c>SecurityException</c>,
    /// <c>UnauthorizedAccessException</c>) is a <c>Failure</c>. Callers that must fail closed on an
    /// inconclusive read (an unknown state must never be treated as "no dependants") use this instead of
    /// <see cref="GetSubKeyNames"/>.
    /// </summary>
    Result<IReadOnlyList<string>> TryReadSubKeyNames(RegistryRoot rootKey, string subKey);
}
