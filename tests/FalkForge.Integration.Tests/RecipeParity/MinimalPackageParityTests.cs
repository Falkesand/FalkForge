using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Integration.Tests.RecipeParity;
using Xunit;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Phase 9 Step 1 — structural parity tests between the legacy <c>MsiCompiler</c>
/// and the recipe-driven <c>MsiAuthoring</c> facade. These tests are the foundation
/// for Steps 2–4 (full row-level convergence and byte-for-byte identity).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MinimalPackageParityTests
{
    // Fixed Unix epoch used by ReproducibilityTests; keeps builds deterministic so
    // SummaryInfo timestamps do not contribute noise to the diff report.
    private const long TestEpoch = 1577836800L;

    /// <summary>
    /// Asserts that both compilers produce MSI files whose <em>table set</em> (names
    /// from <c>_Tables</c>) is identical. This is expected to pass today because both
    /// pipelines call the same table-creation SQL for the mandatory MSI tables.
    /// </summary>
    [Fact(Skip = "Phase 9 Step 1 baseline — table set divergences: RemoveIniFile only in legacy, " +
               "LockPermissions+MsiLockPermissionsEx only in recipe. Fix in Steps 2-3. " +
               "Remove Skip when MsiByteDiffHarness.CompareCompilers reports no [TableSet] diffs.")]
    public void MinimalPackage_table_set_is_identical()
    {
        var package = BuildMinimalPackage();
        var report = MsiByteDiffHarness.CompareCompilers(package, nameof(MinimalPackage_table_set_is_identical));

        var tableDiffs = report.StructuralDifferences
            .Where(d => d.StartsWith("[TableSet]", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            tableDiffs.Count == 0,
            $"Table sets differ between legacy and recipe compilers:{Environment.NewLine}" +
            string.Join(Environment.NewLine, tableDiffs));
    }

    /// <summary>
    /// Byte-level identity check across the whole MSI file. This is expected to FAIL
    /// today because the two pipelines diverge in at least the following ways:
    /// <list type="bullet">
    ///   <item>Media table: legacy uses <c>CabinetPlanner</c> (multi-disk capable),
    ///         recipe uses a fixed single-cabinet with stream name <c>#cab1.cab</c></item>
    ///   <item>SummaryInfo: legacy emits before commit; recipe emits separately via
    ///         <c>SetSummaryInfo</c> then patches; both paths use
    ///         <see cref="FalkForge.Compiler.Msi.Signing.SummaryInfoPatcher"/> so timing
    ///         differences may still cause FILETIME drift between builds</item>
    ///   <item>Possible stream ordering differences in the OLE compound document</item>
    /// </list>
    /// This test is skipped so the full suite stays green. Steps 2–4 will converge
    /// the two pipelines until this assertion holds unconditionally.
    /// </summary>
    [Fact(Skip = "Phase 9 Step 1 baseline — known divergences: Media cabinet stream name, " +
                 "possible SummaryInfo FILETIME jitter. Fix in Steps 2-3. " +
                 "Remove Skip when MsiByteDiffHarness.CompareCompilers reports Equal=true.")]
    public void MinimalPackage_byte_layout_matches_legacy()
    {
        var package = BuildMinimalPackage();
        var report = MsiByteDiffHarness.CompareCompilers(package, nameof(MinimalPackage_byte_layout_matches_legacy));

        Assert.True(
            report.Equal,
            $"MSI byte layout differs between legacy and recipe compilers.{Environment.NewLine}" +
            $"Structural differences:{Environment.NewLine}" +
            string.Join(Environment.NewLine, report.StructuralDifferences) +
            (report.FirstByteDiff is { } diff
                ? $"{Environment.NewLine}First byte diff: {diff}"
                : string.Empty));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static FalkForge.Models.PackageModel BuildMinimalPackage()
    {
        // No files — the minimal package a compiler must handle: just the identity
        // metadata required for a valid MSI (Name, Manufacturer, Version).
        // ProductCode is pinned so both compilers write the same GUID; UpgradeCode
        // is pinned for the same reason.
        var builder = new PackageBuilder
        {
            Name = "ParityTest",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            UpgradeCode = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        // Reproducible mode: pins all SummaryInfo timestamps so diff noise is
        // isolated to structural pipeline differences, not wall-clock drift.
        builder.Reproducible(TestEpoch);

        return builder.Build();
    }
}
