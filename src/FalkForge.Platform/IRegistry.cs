using FalkForge;

namespace FalkForge.Platform;

/// <summary>
/// Outcome of <see cref="IRegistry.TryGetStringValue"/>. Originally wrapped in a struct because
/// <see cref="Result{T}.Success"/> used to reject a null payload unconditionally (its null-guard checked
/// the runtime value, not the nullable annotation), so a legitimate "key/value does not exist" success
/// could not be represented as <c>Result&lt;string?&gt;.Success(null)</c>. That guard is gone (task #42) and
/// <c>Result&lt;string?&gt;</c> now works directly, so this wrapper is redundant; kept as-is for this change
/// to stay small — replacing it is a separate follow-up (touches the interface, both implementations, and
/// their callers/tests).
/// </summary>
public readonly record struct RegistryStringValue(string? Value);

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
    /// exception escape uncaught. A missing key or missing value name is SUCCESS with
    /// <see cref="RegistryStringValue.Value"/> <c>null</c> — only a genuine read error (e.g.
    /// <c>SecurityException</c>, <c>UnauthorizedAccessException</c>) is a <c>Failure</c>. Callers that
    /// must fail closed on an inconclusive read (an unknown state must never look like "value absent")
    /// use this instead of <see cref="GetStringValue"/>.
    /// </summary>
    Result<RegistryStringValue> TryGetStringValue(RegistryRoot rootKey, string subKey, string valueName);
}
