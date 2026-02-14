namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class CustomActionBuilder
{
    private readonly string _id;
    private int _type;
    private string _sourceRef = string.Empty;

    internal CustomActionBuilder(string id) => _id = id;

    public string? Target { get; set; }
    public string? Condition { get; set; }
    public int? Sequence { get; set; }
    public string? After { get; set; }
    public string? Before { get; set; }

    public CustomActionBuilder DllFromBinary(string binaryName, string entryPoint)
    {
        _type = CustomActionType.DllFromBinary;
        _sourceRef = binaryName;
        Target = entryPoint;
        return this;
    }

    public CustomActionBuilder ExeFromBinary(string binaryName)
    {
        _type = CustomActionType.ExeFromBinary;
        _sourceRef = binaryName;
        return this;
    }

    public CustomActionBuilder SetProperty(string propertyName, string value)
    {
        _type = CustomActionType.SetProperty;
        _sourceRef = propertyName;
        Target = value;
        return this;
    }

    internal CustomActionModel Build() => new()
    {
        Id = _id,
        Type = _type,
        SourceRef = _sourceRef,
        Target = Target,
        Condition = Condition,
        Sequence = Sequence,
        After = After,
        Before = Before
    };
}
