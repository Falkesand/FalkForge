namespace FalkForge.Ui.Abstractions;

using System.Collections.Concurrent;

public sealed class InstallerState
{
    private const string InstallDirectoryKey = "InstallDirectory";
    private readonly ConcurrentDictionary<string, object> _values = new();

    public string? InstallDirectory
    {
        get => Get<string>(InstallDirectoryKey);
        set
        {
            if (value is null)
                _values.TryRemove(InstallDirectoryKey, out _);
            else
                Set(InstallDirectoryKey, value);
        }
    }

    public T? Get<T>(string key)
    {
        if (_values.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public void Set<T>(string key, T value) where T : notnull
    {
        _values[key] = value;
    }

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool Remove(string key) => _values.TryRemove(key, out _);
}
