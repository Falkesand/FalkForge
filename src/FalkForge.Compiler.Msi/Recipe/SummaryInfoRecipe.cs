namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable description of the OLE summary-information stream that will
/// be written to the produced MSI. Mirrors the WinSDK summary-info property
/// IDs used by <c>SummaryInfoWriter</c>; values are validated and timestamped
/// downstream by the executor.
/// </summary>
public sealed record SummaryInfoRecipe
{
    public required string Title { get; init; }
    public required string Subject { get; init; }
    public required string Author { get; init; }
    public required string Template { get; init; }
    public required string Keywords { get; init; }
    public required string Comments { get; init; }
    public required int RevisionNumber { get; init; }
    public required int CodePage { get; init; }
}
