namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Strategy controlling how the recipe builder assigns sequence numbers to
/// files in <see cref="MsiDatabaseRecipe.FileSequencing"/>. Sequencing must be
/// deterministic for reproducible builds; both strategies guarantee that
/// property.
/// </summary>
public enum FileSequencingStrategy
{
    /// <summary>Order files by their MSI File primary key (ordinal string compare).</summary>
    FileIdOrdinal = 0,

    /// <summary>Group files by their owning Component, then order by File id within each component.</summary>
    ComponentThenFileId = 1,
}
