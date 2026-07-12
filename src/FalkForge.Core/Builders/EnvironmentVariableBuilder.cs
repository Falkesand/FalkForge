using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class EnvironmentVariableBuilder
{
    private readonly string _name;
    private readonly string _value;

    internal EnvironmentVariableBuilder(string name, string value)
    {
        _name = name;
        _value = value;
    }

    public bool IsSystem { get; set; } = true;
    public EnvironmentVariableAction Action { get; set; } = EnvironmentVariableAction.Set;
    public string? Separator { get; set; }

    /// <summary>
    /// WiX-style value placement (<see cref="EnvironmentVariablePart"/>: all/first/last). When set,
    /// it governs where the authored value sits relative to any existing value via the MSI
    /// <c>[~]</c> token. Prefer the <see cref="Set"/>/<see cref="Append"/>/<see cref="Prepend"/>
    /// convenience methods, which keep this and <see cref="Action"/> consistent.
    /// </summary>
    public string? Part { get; set; }

    /// <summary>
    /// Replace the whole variable value (create or overwrite). This is the default behaviour.
    /// </summary>
    public EnvironmentVariableBuilder Set()
    {
        Action = EnvironmentVariableAction.Set;
        Part = EnvironmentVariablePart.All;
        return this;
    }

    /// <summary>
    /// Append the authored value after any existing value (e.g. adding a directory to the end of
    /// <c>PATH</c>). Encodes to <c>[~]&lt;separator&gt;&lt;value&gt;</c> in the MSI Environment table.
    /// </summary>
    /// <param name="separator">Optional separator inserted between the existing value and the new
    /// text; defaults to <c>;</c> when omitted.</param>
    public EnvironmentVariableBuilder Append(string? separator = null)
    {
        Action = EnvironmentVariableAction.Append;
        Part = EnvironmentVariablePart.Last;
        if (separator is not null)
        {
            Separator = separator;
        }

        return this;
    }

    /// <summary>
    /// Prepend the authored value ahead of any existing value (e.g. putting a directory at the
    /// front of <c>PATH</c>). Encodes to <c>&lt;value&gt;&lt;separator&gt;[~]</c> in the MSI
    /// Environment table.
    /// </summary>
    /// <param name="separator">Optional separator inserted between the new text and the existing
    /// value; defaults to <c>;</c> when omitted.</param>
    public EnvironmentVariableBuilder Prepend(string? separator = null)
    {
        Action = EnvironmentVariableAction.Prepend;
        Part = EnvironmentVariablePart.First;
        if (separator is not null)
        {
            Separator = separator;
        }

        return this;
    }

    internal EnvironmentVariableModel Build()
    {
        return new EnvironmentVariableModel
        {
            Name = _name,
            Value = _value,
            IsSystem = IsSystem,
            Action = Action,
            Part = Part,
            Separator = Separator
        };
    }
}