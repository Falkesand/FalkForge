namespace FalkForge.Ui.Abstractions;

using System.Collections.Concurrent;
using System.Security.Cryptography;

public sealed class InstallerState : IDisposable
{
    private const string InstallDirectoryKey = "InstallDirectory";
    private readonly ConcurrentDictionary<string, object> _values = new();
    private readonly ConcurrentDictionary<string, byte[]> _sensitiveValues = new();
    private readonly ISensitiveDataProtector? _protector;
    private bool _disposed;

    public InstallerState()
    {
    }

    public InstallerState(ISensitiveDataProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

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

    public void SetSensitive(string key, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_protector is null)
            throw new InvalidOperationException(
                "Sensitive storage requires an ISensitiveDataProtector. " +
                "Use the constructor that accepts an ISensitiveDataProtector.");

        var plainCopy = data.ToArray();
        byte[] protectedData;
        try
        {
            protectedData = _protector.Protect(plainCopy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainCopy);
        }

        _sensitiveValues.AddOrUpdate(
            key,
            _ => protectedData,
            (_, old) =>
            {
                CryptographicOperations.ZeroMemory(old);
                return protectedData;
            });
    }

    public SensitiveBytes GetSensitive(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sensitiveValues.TryGetValue(key, out var protectedData) || _protector is null)
            return default;

        var plain = _protector.Unprotect(protectedData);
        return new SensitiveBytes(plain);
    }

    public bool RemoveSensitive(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sensitiveValues.TryRemove(key, out var protectedData))
            return false;

        CryptographicOperations.ZeroMemory(protectedData);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _sensitiveValues)
        {
            CryptographicOperations.ZeroMemory(kvp.Value);
        }

        _sensitiveValues.Clear();
    }
}
