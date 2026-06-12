namespace FalkForge.Cli.Verification;

/// <summary>
/// Outcome of byte-comparing a rebuilt artifact against a shipped artifact.
/// Carries enough detail for an actionable verification diagnostic: whether the bytes
/// are identical, the size delta, the total number of differing bytes, the first few
/// differing offsets, and an optional human-readable hint about which structural region
/// of the artifact the first difference falls in (bundles only).
/// </summary>
/// <param name="Identical">True when both files have identical length and content.</param>
/// <param name="ExpectedSize">Length in bytes of the shipped (expected) artifact.</param>
/// <param name="ActualSize">Length in bytes of the rebuilt (actual) artifact.</param>
/// <param name="SizeDelta">ActualSize − ExpectedSize (signed: positive = rebuilt is larger).</param>
/// <param name="DifferingByteCount">
/// Total count of byte positions that differ, including positions that exist in only one
/// file (i.e. the length difference contributes to this count).
/// </param>
/// <param name="FirstDifferingOffsets">
/// The first N differing offsets (N = the comparer's <c>maxOffsets</c> cap), in ascending order.
/// </param>
/// <param name="RegionHint">
/// Optional structural hint (e.g. "manifest", "payload", "TOC", "footer") describing where the
/// first differing offset falls. Null when no hint could be derived (e.g. for MSI artifacts).
/// </param>
public sealed record ComparisonReport(
    bool Identical,
    long ExpectedSize,
    long ActualSize,
    long SizeDelta,
    long DifferingByteCount,
    IReadOnlyList<long> FirstDifferingOffsets,
    string? RegionHint = null);
