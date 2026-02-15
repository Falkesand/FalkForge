namespace FalkInstaller.Engine.Tests.Mocks;

using FalkInstaller.Platform;

public sealed class MockRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, object?>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public MockRegistry AddKey(string rootKey, string subKey)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        if (!_keys.ContainsKey(fullKey))
        {
            _keys[fullKey] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return this;
    }

    public MockRegistry SetStringValue(string rootKey, string subKey, string valueName, string value)
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

    public MockRegistry SetDWordValue(string rootKey, string subKey, string valueName, int value)
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

    public bool KeyExists(string rootKey, string subKey)
    {
        var fullKey = $@"{rootKey}\{subKey}";
        return _keys.ContainsKey(fullKey);
    }

    public string? GetStringValue(string rootKey, string subKey, string valueName)
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

    public int? GetDWordValue(string rootKey, string subKey, string valueName)
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

    public IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey)
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
}
