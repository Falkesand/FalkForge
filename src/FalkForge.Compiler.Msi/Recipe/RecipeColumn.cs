using System.Text.RegularExpressions;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Schema descriptor for one column in a <see cref="RecipeTable"/>. The
/// <see cref="Name"/> property is validated at construction against MSI
/// identifier rules (same regex as <see cref="TableId"/>) — this defends
/// SQL emission code against identifier injection without an extra sweep.
/// Construction is internal-facing; invalid names throw
/// <see cref="ArgumentException"/> rather than returning a <c>Result</c>.
/// </summary>
public sealed record RecipeColumn
{
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly string _name = string.Empty;

    public required string Name
    {
        get => _name;
        init => _name = ValidateName(value);
    }

    public required ColumnType Type { get; init; }
    public required int Width { get; init; }
    public required bool Nullable { get; init; }
    public required bool LocalizableKey { get; init; }

    private static string ValidateName(string value)
    {
        if (value is null)
        {
            throw new ArgumentException("Column name cannot be null.", nameof(value));
        }

        if (value.Length == 0)
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(value));
        }

        if (value.Length > 31)
        {
            throw new ArgumentException(
                $"Column name '{value}' exceeds 31 characters (MSI maximum).",
                nameof(value));
        }

        if (!IdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException(
                $"Column name '{value}' is not a valid MSI identifier (must match ^[A-Za-z_][A-Za-z0-9_]{{0,30}}$).",
                nameof(value));
        }

        return value;
    }
}
