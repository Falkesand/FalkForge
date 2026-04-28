using System.Text.RegularExpressions;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Identifier of an MSI table. Values are validated at construction
/// against MSI table-name rules: must match <c>^[A-Za-z_][A-Za-z0-9_]{0,30}$</c>.
/// This is the single defense point against SQL identifier injection in the
/// recipe pipeline. Once a <see cref="TableId"/> exists, downstream code can
/// safely interpolate <see cref="Value"/> into <c>CREATE TABLE</c> /
/// <c>SELECT</c> SQL strings without further escaping.
/// </summary>
public readonly record struct TableId
{
    // Anchored regex enforcing MSI Identifier rules:
    //   - first char letter or underscore
    //   - subsequent chars letter, digit, or underscore
    //   - total length 1..31 (MSI maximum table-name length)
    private static readonly Regex IdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>The validated table name. Never null, never empty for a valid instance.</summary>
    public string Value { get; }

    private TableId(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Validates <paramref name="name"/> as an MSI table identifier and wraps
    /// it as a <see cref="TableId"/>. Returns <see cref="ErrorKind.Validation"/>
    /// failure for null, empty, too-long (32+), or any name containing
    /// characters outside <c>[A-Za-z0-9_]</c> or starting with a digit.
    /// </summary>
    public static Result<TableId> Create(string name)
    {
        if (name is null)
        {
            return Result<TableId>.Failure(ErrorKind.Validation, "Table name cannot be null.");
        }

        if (name.Length == 0)
        {
            return Result<TableId>.Failure(ErrorKind.Validation, "Table name cannot be empty.");
        }

        if (name.Length > 31)
        {
            return Result<TableId>.Failure(
                ErrorKind.Validation,
                $"Table name '{name}' exceeds 31 characters (MSI maximum).");
        }

        if (!IdentifierPattern.IsMatch(name))
        {
            return Result<TableId>.Failure(
                ErrorKind.Validation,
                $"Table name '{name}' is not a valid MSI identifier (must match ^[A-Za-z_][A-Za-z0-9_]{{0,30}}$).");
        }

        return Result<TableId>.Success(new TableId(name));
    }

    public override string ToString() => Value ?? string.Empty;
}
