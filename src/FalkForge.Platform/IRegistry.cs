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
    /// (access denied / unreadable / malformed name) instead of silently collapsing it to an empty
    /// list. A missing key is SUCCESS with an empty list — only a genuine read error (e.g.
    /// <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) or a malformed key name (e.g.
    /// <c>ArgumentException</c> for a path component over 255 characters) is a <c>Failure</c>. Callers
    /// that must fail closed on an inconclusive read (an unknown state must never be treated as "no
    /// dependants") use this instead of <see cref="GetSubKeyNames"/>.
    /// </summary>
    Result<IReadOnlyList<string>> TryReadSubKeyNames(RegistryRoot rootKey, string subKey);

    /// <summary>
    /// Reads a string value, reporting a read failure (access denied / unreadable / malformed name)
    /// instead of letting an exception escape uncaught. Both <c>REG_SZ</c> and <c>REG_EXPAND_SZ</c>
    /// values come back as a string — the underlying <c>RegistryKey.GetValue</c> call expands
    /// environment variables in a <c>REG_EXPAND_SZ</c> value by default, so a value stored as
    /// <c>%ProgramFiles%\App</c> is returned already expanded (e.g. <c>C:\Program Files\App</c>), never
    /// the literal placeholder — a search condition that compares against the literal placeholder will
    /// never match. A missing key, a missing value name, or a value that exists but is not a
    /// string-returning type (e.g. <c>REG_DWORD</c>, <c>REG_QWORD</c>, <c>REG_BINARY</c>,
    /// <c>REG_MULTI_SZ</c>) is SUCCESS with <c>null</c> — only a genuine read error (e.g.
    /// <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) or a malformed key name (e.g.
    /// <c>ArgumentException</c> for a path component over 255 characters) is a <c>Failure</c>. Callers
    /// that must fail closed on an inconclusive read (an unknown state must never look like "value
    /// absent") use this instead of <see cref="GetStringValue"/>.
    /// </summary>
    Result<string?> TryGetStringValue(RegistryRoot rootKey, string subKey, string valueName);

    /// <summary>
    /// Reads a REG_DWORD value, reporting a read failure (access denied / unreadable) instead of letting
    /// an exception escape uncaught. A missing key, a missing value name, or a value that exists but is
    /// not a REG_DWORD (e.g. REG_SZ, REG_QWORD, REG_BINARY, REG_MULTI_SZ) is SUCCESS with <c>null</c> —
    /// only a genuine read error (e.g. <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) is a
    /// <c>Failure</c>. Callers that need the numeric value for a comparison (an existence check alone,
    /// regardless of type, wants <see cref="TryValueExists"/> instead) and must fail closed on an
    /// inconclusive read use this instead of <see cref="GetDWordValue"/>.
    /// <para>
    /// <b>Signed, not unsigned:</b> a REG_DWORD is an unsigned 32-bit value, but this returns
    /// <see cref="int"/> (the .NET registry API's own <c>as int?</c> cast). A stored value at or above
    /// <c>0x80000000</c> comes back negative. No FalkForge built-in prerequisite or documented search
    /// condition currently stores a value in that range, so this has not needed fixing; a caller
    /// comparing against a literal parsed as unsigned/<see cref="long"/> (as
    /// <c>SearchConditionEvaluator.EvaluateRegistryDWordComparison</c> does) must be aware the read side
    /// can go negative before the write side's literal does.
    /// </para>
    /// </summary>
    Result<int?> TryGetDWordValue(RegistryRoot rootKey, string subKey, string valueName);

    /// <summary>
    /// Reports whether a named value exists under <paramref name="subKey"/>, regardless of its
    /// registry type (REG_SZ, REG_DWORD, REG_MULTI_SZ, ...) — unlike <see cref="TryGetStringValue"/>,
    /// which returns a string for REG_SZ and REG_EXPAND_SZ but reports SUCCESS with <c>null</c> for
    /// every other type (REG_DWORD, REG_QWORD, REG_BINARY, REG_MULTI_SZ) as if the value were absent.
    /// Reports a read failure (access denied / unreadable / malformed name) instead of letting an
    /// exception escape uncaught. A missing key or a missing value name is SUCCESS with <c>false</c> —
    /// only a genuine read error (e.g. <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) or
    /// a malformed key name (e.g. <c>ArgumentException</c> for a path component over 255 characters) is
    /// a <c>Failure</c>. Callers that must fail closed on an inconclusive read (an unknown state must
    /// never look like "value absent") use this instead of a bare existence check built on
    /// <see cref="KeyExists"/> or <see cref="GetStringValue"/>.
    /// </summary>
    Result<bool> TryValueExists(RegistryRoot rootKey, string subKey, string valueName);

    /// <summary>
    /// Reports whether <paramref name="subKey"/> exists, reporting a read failure (access denied /
    /// unreadable / malformed name) instead of letting an exception escape uncaught. A missing key is
    /// SUCCESS with <c>false</c> — only a genuine read error (e.g. <c>SecurityException</c>,
    /// <c>UnauthorizedAccessException</c>) or a malformed key name (e.g. <c>ArgumentException</c> for a
    /// path component over 255 characters — an authoring mistake, not an access-control failure) is a
    /// <c>Failure</c>. Callers that must fail closed on an inconclusive read (an unknown state must
    /// never look like "key absent") use this instead of the bare <see cref="KeyExists"/>, which cannot
    /// report a read failure at all: <c>MockRegistry</c>'s implementation returns <c>false</c> for an
    /// unreadable key, while <c>WindowsRegistry</c>'s implementation lets the exception escape uncaught.
    /// </summary>
    Result<bool> TryKeyExists(RegistryRoot rootKey, string subKey);
}
