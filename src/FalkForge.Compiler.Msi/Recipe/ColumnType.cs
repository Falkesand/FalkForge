namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Storage class of a recipe column. Drives both schema emission
/// (<c>CREATE TABLE</c>) and cell-value assignment in the executor.
/// </summary>
public enum ColumnType
{
    /// <summary>32-bit signed integer cell.</summary>
    Integer,

    /// <summary>Plain string cell, stored as UTF-16 in the MSI string pool.</summary>
    String,

    /// <summary>Localized string cell, eligible for transform-based localization.</summary>
    Localized,

    /// <summary>Binary stream reference cell. Cell stores a stream name; bytes live in <see cref="MsiDatabaseRecipe.Streams"/>.</summary>
    Binary
}
