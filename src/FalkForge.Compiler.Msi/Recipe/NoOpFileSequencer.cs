using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// No-op <see cref="IFileSequencer"/> that returns an empty sequence array
/// regardless of input. Used as the default sequencer in phase 3 before any
/// real producers are wired up; later phases substitute strategies that
/// honour <see cref="MsiRecipeBuildOptions.Sequencing"/>.
/// </summary>
internal sealed class NoOpFileSequencer : IFileSequencer
{
    public ImmutableArray<FileSequenceEntry> Sequence(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ImmutableArray<FileSequenceEntry>.Empty;
    }
}
