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

    private EnvironmentVariableAction _action = EnvironmentVariableAction.Set;

    public bool IsSystem { get; set; } = true;

    /// <summary>
    /// The variable operation (set / append / prepend). Assigning this directly clears any
    /// previously chosen <see cref="Part"/> so the two can never silently disagree — the encoder
    /// treats a non-null <see cref="Part"/> as authoritative, so a stale Part would otherwise mask a
    /// freshly-set Action. Use <see cref="Set"/>/<see cref="Append"/>/<see cref="Prepend"/> to set
    /// both together.
    /// </summary>
    public EnvironmentVariableAction Action
    {
        get => _action;
        set
        {
            _action = value;
            Part = null;
        }
    }

    public string? Separator { get; set; }

    /// <summary>
    /// WiX-style value placement (<see cref="EnvironmentVariablePart"/>: all/first/last). When set,
    /// it governs where the authored value sits relative to any existing value via the MSI
    /// <c>[~]</c> token and overrides <see cref="Action"/>. Prefer the <see cref="Set"/>/
    /// <see cref="Append"/>/<see cref="Prepend"/> convenience methods, which set this and
    /// <see cref="Action"/> together.
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