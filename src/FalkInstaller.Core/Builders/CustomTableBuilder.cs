namespace FalkInstaller.Builders;

using System.Text.RegularExpressions;
using FalkInstaller.Models;

public sealed class CustomTableBuilder
{
    private static readonly Regex ColumnNameRegex =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private string _name = string.Empty;
    private readonly List<CustomTableColumnModel> _columns = [];
    private readonly List<Dictionary<string, object?>> _rows = [];

    public CustomTableBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public CustomTableBuilder Column(string name, CustomTableColumnType type, Action<ColumnOptions>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Column name must not be empty.", nameof(name));

        if (!ColumnNameRegex.IsMatch(name))
            throw new ArgumentException(
                $"Column name '{name}' is invalid. Column names must start with a letter or underscore and contain only alphanumeric characters and underscores.",
                nameof(name));

        var options = new ColumnOptions();
        configure?.Invoke(options);

        _columns.Add(new CustomTableColumnModel
        {
            Name = name,
            Type = type,
            PrimaryKey = options.IsPrimaryKey,
            Nullable = options.IsNullable,
            Width = options.ColumnWidth,
            LocalizedDescription = options.Description
        });

        return this;
    }

    public CustomTableBuilder Row(Action<RowBuilder> configure)
    {
        var builder = new RowBuilder();
        configure(builder);
        _rows.Add(builder.Build());
        return this;
    }

    internal CustomTableModel Build() => new()
    {
        Name = _name,
        Columns = _columns,
        Rows = _rows
    };
}
