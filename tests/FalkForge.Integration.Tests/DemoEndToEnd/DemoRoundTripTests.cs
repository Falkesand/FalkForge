using System.Runtime.Versioning;
using FalkForge.Decompiler;
using Xunit;

namespace FalkForge.Integration.Tests.DemoEndToEnd;

/// <summary>
/// Phase 14 — Recipe round-trip tests for demo fixtures.
///
/// For each qualifying MSI demo:
///   1. Compile the demo via <c>dotnet run</c> (delegated to <see cref="DemoBuildFixture"/>).
///   2. Open the produced .msi with <see cref="MsiDecompiler.DecompileToRecipe"/>.
///   3. Assert the read succeeds and the core table collections are populated.
///
/// Byte-identical (reproducible-build) tier:
///   Skipped for all demos. Only one demo calls <c>.Reproducible()</c>, and the
///   compiler-level byte-identity assertion is already covered by
///   <see cref="RecipeParity.IntegrationParitySweep"/>. Applying it to demo
///   fixtures would require every demo to opt in to a pinned SOURCE_DATE_EPOCH
///   and fixed ProductCode, which is a demo-authoring concern, not a read-pipeline
///   concern. This is documented here rather than silently omitting the tier.
/// </summary>
[Collection("DemoEndToEnd")]
[SupportedOSPlatform("windows")]
[Trait("Category", "E2E")]
public sealed class DemoRoundTripTests
{
    private readonly DemoBuildFixture _fixture;

    public DemoRoundTripTests(DemoBuildFixture fixture) => _fixture = fixture;

    // -------------------------------------------------------------------------
    // Theory data — MSI-producing demos only, infrastructure-required excluded
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> RoundTripMsiDemosData =>
        DemoTestCatalog.MsiDemos
            .Where(d => !d.RequiresInfrastructure)
            .Select(d => new object[] { d });

    // -------------------------------------------------------------------------
    // Round-trip: compile → DecompileToRecipe → assert core collections present
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(RoundTripMsiDemosData))]
    public void Msi_DecompileToRecipe_Succeeds(DemoExpectation demo)
    {
        E2EGate.SkipUnlessOptedIn();

        // Skip demos whose MSIs are known-empty due to a write-side bug outside Phase 14 scope.
        // FeatureBuilder._files are silently dropped in PackageBuilder.Feature(): files added via
        // feature.Files(...) never reach the compiled MSI. Tracked as a separate compiler fix.
        if (demo.RoundTripSkipReason is not null)
            Assert.Skip(demo.RoundTripSkipReason);

        // Step 1 — build (cached by fixture; zero cost on second Theory call)
        var build = _fixture.GetOrBuild(demo);
        Assert.True(build.Succeeded,
            $"Demo '{demo.Name}' did not build successfully — cannot run round-trip.\n" +
            $"Exit code: {build.ExitCode}\nStderr: {build.Stderr}");

        var msiPath = build.OutputFile!;
        Assert.True(File.Exists(msiPath),
            $"Demo '{demo.Name}' build result references '{msiPath}' but file not found on disk.");

        // Step 2 — open MSI and read raw recipe (no reconstructor stage)
        var decompiler = new MsiDecompiler();
        var result = decompiler.DecompileToRecipe(msiPath);

        // Step 3 — assert read pipeline succeeded
        Assert.True(result.IsSuccess,
            $"DecompileToRecipe failed for demo '{demo.Name}': " +
            (result.IsFailure ? result.Error.ToString() : string.Empty));

        var recipe = result.Value;

        // Every MSI must have at least one Property row (ProductName etc.)
        Assert.True(recipe.Properties.Count > 0,
            $"Demo '{demo.Name}': Property table is empty — decompiler produced no property rows.");

        // Every MSI with files must have Component and File rows
        if (demo.RequiredTables.Contains("File", StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(recipe.Files.Count > 0,
                $"Demo '{demo.Name}': File table expected but read 0 rows.");
            Assert.True(recipe.Components.Count > 0,
                $"Demo '{demo.Name}': Component table expected (implied by files) but read 0 rows.");
        }

        // Feature table must always be present (MSI requires at least one feature)
        Assert.True(recipe.Features.Count > 0,
            $"Demo '{demo.Name}': Feature table is empty — every MSI must have at least one feature.");

        // Service table check
        if (demo.RequiredTables.Contains("ServiceInstall", StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(recipe.Services.Count > 0,
                $"Demo '{demo.Name}': ServiceInstall table expected but read 0 rows.");
        }

        // Upgrade / MajorUpgrade table check
        if (demo.RequiredTables.Contains("Upgrade", StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(recipe.Upgrades.Count > 0,
                $"Demo '{demo.Name}': Upgrade table expected but read 0 rows.");
        }
    }
}
