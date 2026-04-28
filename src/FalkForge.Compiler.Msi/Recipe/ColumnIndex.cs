namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Zero-based index of a column inside a <see cref="RecipeTable"/>'s column list.
/// Wraps a non-negative <see cref="int"/>; the constructor rejects negative
/// values with <see cref="ArgumentOutOfRangeException"/>.
/// </summary>
public readonly record struct ColumnIndex
{
    public int Value { get; }

    public ColumnIndex(int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "ColumnIndex must be non-negative.");
        }

        Value = value;
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
