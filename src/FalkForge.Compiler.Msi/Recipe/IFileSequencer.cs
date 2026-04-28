using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Strategy that assigns deterministic sequence numbers to files for
/// <see cref="MsiDatabaseRecipe.FileSequencing"/>. Implementations consume the
/// resolved package via <see cref="RecipeBuildContext"/> and emit an ordered
/// array of <see cref="FileSequenceEntry"/>. The default phase-3 implementation
/// is a no-op (<see cref="NoOpFileSequencer"/>); real strategies arrive in
/// later phases.
/// </summary>
internal interface IFileSequencer
{
    /// <summary>Compute the sequence-entry array for the given recipe-build context.</summary>
    ImmutableArray<FileSequenceEntry> Sequence(RecipeBuildContext context);
}
