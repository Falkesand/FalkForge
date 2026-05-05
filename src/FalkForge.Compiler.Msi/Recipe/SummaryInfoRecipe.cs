namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Immutable description of the OLE summary-information stream that will
/// be written to the produced MSI. Mirrors every WinSDK summary-info property
/// ID used by <c>SummaryInfoWriter</c>; the executor writes all fields in a
/// single <c>SetSummaryInfo</c> call — no post-apply patches required.
/// </summary>
public sealed record SummaryInfoRecipe
{
    public required string Title { get; init; }
    public required string Subject { get; init; }
    public required string Author { get; init; }
    public required string Template { get; init; }
    public required string Keywords { get; init; }
    public required string Comments { get; init; }

    /// <summary>
    /// PID_REVNUMBER — for MSI databases this is the PackageCode GUID in
    /// registry-format braces, e.g. <c>{AAAAAAAA-BBBB-...}</c>.
    /// </summary>
    public required string RevisionNumber { get; init; }

    public required int CodePage { get; init; }

    /// <summary>PID_APPNAME — application that created the MSI (e.g. "FalkForge").</summary>
    public required string CreatingApplication { get; init; }

    /// <summary>PID_WORDCOUNT — installer capability flags (2 = compressed + LongFileNames).</summary>
    public required int WordCount { get; init; }

    /// <summary>PID_PAGECOUNT — minimum installer version (200 = Windows Installer 2.0).</summary>
    public required int PageCount { get; init; }

    /// <summary>PID_SECURITY — read/write restriction (2 = read-only recommended).</summary>
    public required int Security { get; init; }
}
