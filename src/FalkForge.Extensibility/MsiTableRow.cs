namespace FalkForge.Extensibility;

public sealed class MsiTableRow
{
    private readonly Dictionary<string, object?> _fields = new();

    public MsiTableRow Set(string column, object? value)
    {
        _fields[column] = value;
        return this;
    }

    public object? Get(string column) =>
        _fields.GetValueOrDefault(column);

    public IReadOnlyDictionary<string, object?> Fields => _fields;
}
