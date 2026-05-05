using System.Collections.Immutable;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Captures the result of a byte-level and structural comparison between two
/// MSI files produced by the legacy <c>MsiCompiler</c> and the recipe-driven
/// <c>MsiAuthoring</c> facade.
/// </summary>
/// <param name="Equal">
/// <see langword="true"/> when both files are byte-for-byte identical.
/// </param>
/// <param name="StructuralDifferences">
/// Human-readable lines describing structural divergences (missing tables,
/// row-count mismatches, differing row sets). Each line is prefixed with a
/// tag such as <c>[TableSet]</c> or <c>[Table:Property]</c> so callers can
/// filter by category.
/// </param>
/// <param name="FirstByteDiff">
/// A human-readable summary of the first differing byte offset (offset,
/// hex values, surrounding context), or <see langword="null"/> when the
/// files are identical or structural inspection was skipped.
/// </param>
public sealed record MsiDiffReport(
    bool Equal,
    ImmutableArray<string> StructuralDifferences,
    string? FirstByteDiff);
