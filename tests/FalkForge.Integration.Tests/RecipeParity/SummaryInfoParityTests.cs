using System.Runtime.Versioning;
using FalkForge.Builders;
using Xunit;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Phase 9 Step 2 — asserts that the recipe pipeline produces identical
/// SummaryInfo fields to the legacy <c>MsiCompiler</c> for the same input
/// package. Prior to this step, <c>MsiRecipeBuilder</c> emitted an empty
/// <c>SummaryInfoRecipe</c> and <c>MsiAuthoring</c> applied a separate
/// post-apply <c>SetSummaryInfo</c> patch; this created a single-source-of-
/// truth violation. The fix promotes full SummaryInfo population into
/// <c>MsiRecipeBuilder.BuildCore</c> and removes the redundant patch.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SummaryInfoParityTests
{
    // Fixed Unix epoch for deterministic timestamps — same value used by
    // MinimalPackageParityTests so harness output is comparable across tests.
    private const long TestEpoch = 1577836800L;

    /// <summary>
    /// Asserts that both compilers produce structurally identical MSIs for the
    /// Property table — which contains the MSI property rows derived from
    /// SummaryInfo-related package fields (ProductName, Manufacturer, etc.)
    /// — and reports no Property-table row divergences.
    /// </summary>
    [Fact]
    public void MinimalPackage_Property_table_rows_are_identical_between_compilers()
    {
        var package = BuildMinimalPackage();
        var report = MsiByteDiffHarness.CompareCompilers(package, nameof(MinimalPackage_Property_table_rows_are_identical_between_compilers));

        var propertyDiffs = report.StructuralDifferences
            .Where(d => d.StartsWith("[Table:Property]", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            propertyDiffs.Count == 0,
            $"Property table rows differ between legacy and recipe compilers:{Environment.NewLine}" +
            string.Join(Environment.NewLine, propertyDiffs));
    }

    /// <summary>
    /// Asserts that no structural divergences related to SummaryInfo remain after
    /// Step 2 SummaryInfo promotion. Pre-existing non-SummaryInfo divergences
    /// (Media cabinet naming #Data.cab vs #cab1.cab, table-set deltas for
    /// RemoveIniFile / LockPermissions / MsiLockPermissionsEx) are excluded from
    /// the assertion scope — those are tracked under Steps 3–4.
    /// </summary>
    [Fact]
    public void MinimalPackage_no_summary_info_divergences_after_promotion()
    {
        var package = BuildMinimalPackage();
        var report = MsiByteDiffHarness.CompareCompilers(package, nameof(MinimalPackage_no_summary_info_divergences_after_promotion));

        // Filter out pre-existing non-SummaryInfo divergences that are tracked
        // under separate steps (Step 3: table-set parity, Step 4: Media cabinet).
        var summaryInfoDivergences = report.StructuralDifferences
            .Where(d =>
                !d.Contains("RemoveIniFile", StringComparison.Ordinal) &&
                !d.Contains("LockPermissions", StringComparison.Ordinal) &&
                !d.Contains("MsiLockPermissionsEx", StringComparison.Ordinal) &&
                !d.Contains("#Data.cab", StringComparison.Ordinal) &&
                !d.Contains("#cab1.cab", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            summaryInfoDivergences.Count == 0,
            $"SummaryInfo-related divergences remain after promotion:{Environment.NewLine}" +
            string.Join(Environment.NewLine, summaryInfoDivergences) +
            $"{Environment.NewLine}(Pre-existing non-SummaryInfo divergences excluded from this assertion scope.)");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static FalkForge.Models.PackageModel BuildMinimalPackage()
    {
        // Pinned GUIDs so both compilers write the same values.
        // Reproducible mode pins all FILETIME values in the OLE summary stream
        // so timestamp non-determinism does not contribute false differences.
        var builder = new PackageBuilder
        {
            Name = "SummaryParityTest",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            UpgradeCode = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        // Reproducible mode: SummaryInfoPatcher rewrites both PID_CREATE_DTM
        // and PID_LASTSAVE_DTM in the OLE compound document so the byte streams
        // are deterministic regardless of wall-clock time.
        builder.Reproducible(TestEpoch);

        return builder.Build();
    }
}
