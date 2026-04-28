namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Pair of MSI File primary key and the cabinet sequence number it occupies.
/// Used by <see cref="MsiDatabaseRecipe.FileSequencing"/> to make the
/// otherwise-implicit contract between the recipe and <c>CabinetBuilder</c>
/// explicit and enforceable.
/// </summary>
public sealed record FileSequenceEntry(string FileId, int Sequence);
