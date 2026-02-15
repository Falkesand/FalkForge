namespace FalkForge.Engine.Variables;

using System.Collections.Concurrent;

public sealed class VariableStore
{
    private readonly ConcurrentDictionary<string, object> _variables =
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
    /// </summary>
    internal object? GetRaw(string name)
    {
        return _variables.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets all variable names. Used for diagnostics.
    /// </summary>
    public IReadOnlyCollection<string> GetNames()
    {
        return _variables.Keys.ToArray();
    }
}
