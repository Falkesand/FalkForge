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

    /// <summary>
    /// Reads a string value, reporting a read failure (access denied / unreadable) instead of letting an
    /// exception escape uncaught. A missing key, a missing value name, or a value that exists but is not
    /// a string type (e.g. REG_DWORD) is SUCCESS with <c>null</c> — only a genuine read error (e.g.
    /// <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) is a <c>Failure</c>. Callers that
    /// must fail closed on an inconclusive read (an unknown state must never look like "value absent")
    /// use this instead of <see cref="GetStringValue"/>.
    /// </summary>
    Result<string?> TryGetStringValue(RegistryRoot rootKey, string subKey, string valueName);

    /// <summary>
    /// Reports whether a named value exists under <paramref name="subKey"/>, regardless of its
    /// registry type (REG_SZ, REG_DWORD, REG_MULTI_SZ, ...) — unlike <see cref="TryGetStringValue"/>,
    /// which recognizes only REG_SZ values and reports SUCCESS with <c>null</c> for any other type as
    /// if the value were absent. Reports a read failure (access denied / unreadable) instead of
    /// letting an exception escape uncaught. A missing key or a missing value name is SUCCESS with
    /// <c>false</c> — only a genuine read error (e.g. <c>SecurityException</c>,
    /// <c>UnauthorizedAccessException</c>) is a <c>Failure</c>. Callers that must fail closed on an
    /// inconclusive read (an unknown state must never look like "value absent") use this instead of a
    /// bare existence check built on <see cref="KeyExists"/> or <see cref="GetStringValue"/>.
    /// </summary>
    Result<bool> TryValueExists(RegistryRoot rootKey, string subKey, string valueName);

    /// <summary>
    /// Reports whether <paramref name="subKey"/> exists, reporting a read failure (access denied /
    /// unreadable) instead of letting an exception escape uncaught. A missing key is SUCCESS with
    /// <c>false</c> — only a genuine read error (e.g. <c>SecurityException</c>,
    /// <c>UnauthorizedAccessException</c>) is a <c>Failure</c>. Callers that must fail closed on an
    /// inconclusive read (an unknown state must never look like "key absent") use this instead of the
    /// bare <see cref="KeyExists"/>, which cannot report a read failure at all: <c>MockRegistry</c>'s
    /// implementation returns <c>false</c> for an unreadable key, while <c>WindowsRegistry</c>'s
    /// implementation lets the exception escape uncaught.
    /// </summary>
    Result<bool> TryKeyExists(RegistryRoot rootKey, string subKey);
}
