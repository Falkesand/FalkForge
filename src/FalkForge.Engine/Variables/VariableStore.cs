namespace FalkForge.Engine.Variables;

using System.Collections.Concurrent;

public sealed class VariableStore : IDisposable
{
    private readonly ConcurrentDictionary<string, object> _variables =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SecureVariable> _secrets =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    public void Set(string name, long value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _variables[name] = value;
    }

    public void Set(string name, Version value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _variables[name] = value;
    }

    public bool Contains(string name)
    {
        return _variables.ContainsKey(name);
    }

    public Result<T> TryGet<T>(string name)
    {
        if (!_variables.TryGetValue(name, out var raw))
        {
            return Result<T>.Failure(ErrorKind.Validation, $"Variable '{name}' not found");
        }

        if (raw is T typed)
        {
            return typed;
        }

        return Result<T>.Failure(ErrorKind.Validation,
            $"Variable '{name}' is {raw.GetType().Name}, not {typeof(T).Name}");
    }

    public Result<string> GetString(string name)
    {
        if (!_variables.TryGetValue(name, out var raw))
        {
            return Result<string>.Failure(ErrorKind.Validation, $"Variable '{name}' not found");
        }

        return raw switch
        {
            string s => s,
            long l => l.ToString(),
            Version v => v.ToString(),
            _ => raw.ToString() ?? string.Empty
        };
    }

    public Result<long> GetInt(string name)
    {
        if (!_variables.TryGetValue(name, out var raw))
        {
            return Result<long>.Failure(ErrorKind.Validation, $"Variable '{name}' not found");
        }

        return raw switch
        {
            long l => l,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => Result<long>.Failure(ErrorKind.Validation,
                $"Variable '{name}' cannot be converted to integer")
        };
    }

    public Result<Version> GetVersion(string name)
    {
        if (!_variables.TryGetValue(name, out var raw))
        {
            return Result<Version>.Failure(ErrorKind.Validation, $"Variable '{name}' not found");
        }

        return raw switch
        {
            Version v => v,
            string s when Version.TryParse(s, out var parsed) => parsed,
            _ => Result<Version>.Failure(ErrorKind.Validation,
                $"Variable '{name}' cannot be converted to Version")
        };
    }

    /// <summary>
    /// Gets the raw stored value for a variable, or null if not found.
    /// Used by the condition evaluator for type-aware comparisons.
    /// Falls back to secret variables when not found in regular variables.
    /// </summary>
    internal object? GetRaw(string name)
    {
        if (_variables.TryGetValue(name, out var value))
        {
            return value;
        }

        if (_secrets.TryGetValue(name, out var secure))
        {
            return secure.GetValue();
        }

        return null;
    }

    /// <summary>
    /// Gets all variable names. Used for diagnostics.
    /// </summary>
    public IReadOnlyCollection<string> GetNames()
    {
        return _variables.Keys.ToArray();
    }

    /// <summary>
    /// Stores a secret variable with pinned, zeroed-on-dispose memory.
    /// Secret variables are excluded from logging and diagnostics.
    /// </summary>
    public void SetSecret(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);

        var secure = new SecureVariable(value);
        SecureVariable? displaced = null;
        _secrets.AddOrUpdate(
            name,
            addValueFactory: _ => secure,
            updateValueFactory: (_, previous) => { displaced = previous; return secure; });
        displaced?.Dispose();
    }

    /// <summary>
    /// Retrieves a secret variable value.
    /// </summary>
    public Result<string> GetSecret(string name)
    {
        if (!_secrets.TryGetValue(name, out var secure))
        {
            return Result<string>.Failure(ErrorKind.Validation, $"Secret variable '{name}' not found");
        }

        return secure.GetValue();
    }

    /// <summary>
    /// Returns true if the named variable is a secret.
    /// </summary>
    public bool IsSecret(string name)
    {
        return _secrets.ContainsKey(name);
    }

    /// <summary>
    /// Disposes all secret variables, zeroing their memory, and clears the secret stores.
    /// </summary>
    public void DisposeSecrets()
    {
        foreach (var kvp in _secrets)
        {
            kvp.Value.Dispose();
        }

        _secrets.Clear();
    }

    /// <summary>
    /// Disposes the variable store, zeroing all secret variable memory.
    /// </summary>
    public void Dispose()
    {
        DisposeSecrets();
    }
}
