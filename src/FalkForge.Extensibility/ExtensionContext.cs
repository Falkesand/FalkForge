using FalkForge.Models;

namespace FalkForge.Extensibility;

public sealed class ExtensionContext
{
    public required PackageModel Package { get; init; }
    public required string OutputDirectory { get; init; }
    public required string SourceDirectory { get; init; }

    /// <summary>
    /// Files the compiler resolved for this package, in emission order, with the generated
    /// <c>File</c> / <c>Component</c> table keys attached. Populated by the MSI recipe builder
    /// before any <see cref="IMsiTableContributor.GetRows"/> call, so a contributor whose table
    /// carries external keys into File or Component can DERIVE them from a file the author
    /// declared instead of asking for a value the author cannot know. Empty on the
    /// pre-resolution context handed to <see cref="IComponentContributor.GetAdditionalFiles"/>,
    /// which by definition runs before file resolution.
    /// </summary>
    public IReadOnlyList<ResolvedFileRef> ResolvedFiles { get; init; } = [];

    /// <summary>
    /// Primary keys of every <c>Component</c> row the compiler resolved, in emission order —
    /// including components that own no file (feature-gated services, registry entries, …).
    /// A contributor whose row needs an owning component but has no file to key off can fall
    /// back to the first entry, mirroring the built-in <c>CreateFolder</c> producer.
    /// </summary>
    public IReadOnlyList<string> ResolvedComponentIds { get; init; } = [];
}
