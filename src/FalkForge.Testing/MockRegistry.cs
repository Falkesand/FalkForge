using FalkForge.Platform;

namespace FalkForge.Testing;

public sealed class MockRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, object?>> _keys = new(StringComparer.OrdinalIgnoreCase);

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
