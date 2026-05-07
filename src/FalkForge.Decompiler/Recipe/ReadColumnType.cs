namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Storage class of a read-side column. Used by <see cref="ReadColumn"/> to drive
/// type-safe parsing in <see cref="ReadRow"/>.
/// </summary>
public enum ReadColumnType
{
    /// <summary>Plain string cell.</summary>
    String,

    /// <summary>32-bit signed integer cell.</summary>
    Integer,
}
