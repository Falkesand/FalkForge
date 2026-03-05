namespace FalkForge.Builders;

public sealed class RowBuilder
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public RowBuilder Set(string column, object? value)
    {
        _values[column] = value;
        return this;
    }

    internal Dictionary<string, object?> Build()
    {
        return new Dictionary<string, object?>(_values, StringComparer.Ordinal);
    }
}