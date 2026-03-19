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

    internal EnvironmentVariableModel Build()
    {
        return new EnvironmentVariableModel
        {
            Name = _name,
            Value = _value,
            IsSystem = IsSystem,
            Action = Action,
            Separator = Separator
        };
    }
}