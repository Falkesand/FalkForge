namespace FalkInstaller.Builders;

public sealed class ColumnOptions
{
    internal bool IsPrimaryKey { get; private set; }
    internal bool IsNullable { get; private set; }
    internal int ColumnWidth { get; private set; } = 255;
    internal string? Description { get; private set; }

    public ColumnOptions PrimaryKey()
    {
        IsPrimaryKey = true;
        return this;
    }

    public ColumnOptions Nullable()
    {
        IsNullable = true;
        return this;
    }

    public ColumnOptions Width(int width)
    {
        ColumnWidth = width;
        return this;
    }

    public ColumnOptions LocalizedDescription(string description)
    {
        Description = description;
        return this;
    }
}
