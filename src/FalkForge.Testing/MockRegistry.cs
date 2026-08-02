using FalkForge.Platform;

namespace FalkForge.Testing;

public sealed class MockRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, object?>> _keys = new(StringComparer.OrdinalIgnoreCase);

    // Prefixes matched against the BARE subkey path (e.g. "SOFTWARE\Classes\Installer\Dependencies"),
    // with no root prefix — TryReadSubKeyNames/TryGetStringValue compare against their `subKey`
    // parameter directly, never against a root-qualified string. Because the root is never part of the
    // comparison, one prefix simulates a read failure under LocalMachine and CurrentUser alike.
    private readonly List<string> _failReadPrefixes = [];

    /// <summary>
    /// Makes <see cref="TryReadSubKeyNames"/> and <see cref="TryGetStringValue"/> return a
    /// <c>Failure</c> for any subkey path under <paramref name="subKeyPrefix"/>. The match is against the
    /// bare subkey — do NOT pass a root-qualified prefix (e.g. <c>"LocalMachine\SOFTWARE\..."</c>); it
    /// will never match and the simulated failure silently won't trigger. Because the root is never part
    /// of the comparison, <see cref="RegistryRoot.LocalMachine"/> and <see cref="RegistryRoot.CurrentUser"/>
    /// are matched alike. Simulates an access-denied/unreadable registry key for fail-closed tests.
    /// </summary>
    public MockRegistry FailReadsUnder(string subKeyPrefix)
    {
        _failReadPrefixes.Add(subKeyPrefix);
        return this;
    }

    public MockRegistry AddKey(RegistryRoot rootKey, string subKey)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (!_keys.ContainsKey(fullKey))
        {
            _keys[fullKey] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return this;
    }

    public MockRegistry SetStringValue(RegistryRoot rootKey, string subKey, string valueName, string value)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (!_keys.TryGetValue(fullKey, out var values))
        {
            values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _keys[fullKey] = values;
        }

        values[valueName] = value;
        return this;
    }

    public MockRegistry SetDWordValue(RegistryRoot rootKey, string subKey, string valueName, int value)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (!_keys.TryGetValue(fullKey, out var values))
        {
            values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _keys[fullKey] = values;
        }

        values[valueName] = value;
        return this;
    }

    public bool KeyExists(RegistryRoot rootKey, string subKey)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        return _keys.ContainsKey(fullKey);
    }

    public string? GetStringValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (_keys.TryGetValue(fullKey, out var values) &&
            values.TryGetValue(valueName, out var value) &&
            value is string str)
        {
            return str;
        }

        return null;
    }

    public int? GetDWordValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (_keys.TryGetValue(fullKey, out var values) &&
            values.TryGetValue(valueName, out var value) &&
            value is int dword)
        {
            return dword;
        }

        return null;
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryRoot rootKey, string subKey)
    {
        var prefix = $@"{rootKey}\{subKey}\";
        var result = new List<string>();
        foreach (var key in _keys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var remainder = key[prefix.Length..];
                // Only direct children (no further backslash separators)
                if (!remainder.Contains('\\'))
                {
                    result.Add(remainder);
                }
            }
        }

        return result;
    }

    void IRegistry.SetStringValue(RegistryRoot rootKey, string subKey, string valueName, string value)
    {
        SetStringValue(rootKey, subKey, valueName, value);
    }

    public Result<IReadOnlyList<string>> TryReadSubKeyNames(RegistryRoot rootKey, string subKey)
    {
        foreach (var prefix in _failReadPrefixes)
        {
            if (subKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Result<IReadOnlyList<string>>.Failure(ErrorKind.SecurityError,
                    $"Simulated read failure under '{rootKey}\\{subKey}'.");
            }
        }

        return Result<IReadOnlyList<string>>.Success(GetSubKeyNames(rootKey, subKey));
    }

    public Result<string?> TryGetStringValue(RegistryRoot rootKey, string subKey, string valueName)
    {
        foreach (var prefix in _failReadPrefixes)
        {
            if (subKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return Result<string?>.Failure(ErrorKind.SecurityError,
                    $"Simulated read failure under '{rootKey}\\{subKey}'.");
            }
        }

        return Result<string?>.Success(GetStringValue(rootKey, subKey, valueName));
    }

    public void DeleteKey(RegistryRoot rootKey, string subKey)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        var keysToRemove = _keys.Keys
            .Where(k => k.Equals(fullKey, StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith(fullKey + @"\", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _keys.Remove(key);
        }
    }
}
