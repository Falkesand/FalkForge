namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Identifier of an MSI table. Values are validated at construction against MSI table-name
/// rules: a letter or underscore, followed by up to 30 more letters, digits, or underscores
/// (31 characters total, the MSI maximum table-name length -- no dots). This is the single
/// defense point against SQL identifier injection in the recipe pipeline. Once a
/// <see cref="TableId"/> exists, downstream code can safely interpolate <see cref="Value"/>
/// into <c>CREATE TABLE</c> / <c>SELECT</c> SQL strings without further escaping.
///
/// The character-class check delegates to <see cref="FalkForge.MsiIdentifierGrammar.IsValidForWrite"/>
/// -- the same canonical grammar <c>FalkForge.Decompiler.MsiTableAccess</c> and
/// <c>FalkForge.Studio.Inspect.MsiTableReader</c> use on the READ side, via
/// <c>IsValidForRead</c> -- rather than restating a separate character class here. The two
/// sides deliberately diverge in two ways, both intentional:
/// <list type="bullet">
/// <item><description>
/// No dots here. The broader MSI-SQL identifier grammar (and the READ side) permits dots, but
/// FalkForge never authors dotted table names, so the WRITE side keeps rejecting them. This is
/// a genuine per-caller restriction, not a duplicated grammar -- reconciling with the shared
/// base must not loosen it.
/// </description></item>
/// <item><description>
/// The 31-character cap layered on top of the shared base below. This is a real MSI
/// table-name format constraint on names FalkForge itself creates, not a property of the
/// grammar in general (the READ side has no equivalent cap).
/// </description></item>
/// </list>
/// The READ side being the more permissive of the two is intentional: never tighten it to
/// match this type, and never loosen this type to match it.
/// </summary>
public readonly record struct TableId
{
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

        if (!MsiIdentifierGrammar.IsValidForWrite(name))
        {
            return Result<TableId>.Failure(
                ErrorKind.Validation,
                $"Table name '{name}' is not a valid MSI identifier: it must start with a letter or " +
                "underscore and contain only alphanumeric characters and underscores.");
        }

        return Result<TableId>.Success(new TableId(name));
    }

    public override string ToString() => Value ?? string.Empty;
}
