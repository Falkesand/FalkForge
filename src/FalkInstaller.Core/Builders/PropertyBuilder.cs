namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class PropertyBuilder
{
    private readonly string _name;
    private readonly string _value;

    internal PropertyBuilder(string name, string value)
    {
        _name = name;
        _value = value;
    }

    public bool IsSecure { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsHidden { get; set; }

    internal PropertyModel Build() => new()
    {
        Name = _name,
        Value = _value,
        IsSecure = IsSecure,
        IsAdmin = IsAdmin,
        IsHidden = IsHidden
    };
}
