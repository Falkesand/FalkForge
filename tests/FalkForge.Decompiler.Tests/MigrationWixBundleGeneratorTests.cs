using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Tests for MigrationProjectGenerator - bundle (.exe) path, WiX Burn branch.
///
/// WHY these tests matter:
/// When a .exe is NOT a FalkForge native bundle, the migrator falls back to WiX Burn
/// (mirroring `forge decompile`). The WiX path additionally surfaces features that have
/// no FalkForge equivalent (variables, searches, BA data, etc.). The migration must:
///   (a) populate MigrationResult.Unmapped with those features, and
///   (b) list them in MIGRATION-REPORT.md
/// so the migrator knows exactly what to re-add by hand. Without this the loss is silent.
///
/// WiX Burn access is Windows-only (PE/.wixburn parsing), so these tests are gated to
/// Windows and use an injected IWixBurnAccess mock.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MigrationWixBundleGeneratorTests
{
    private const string Ns = "http://schemas.microsoft.com/wix/2008/Burn";
    private const string DummySourcePath = "../../src";
    private const string ProjectName = "WixMigrated";

    private static readonly Guid TestBundleId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // A Burn Search element has no FalkForge equivalent, so WixManifestMapper records it
    // as an unmapped feature (unlike Variable, which IS mapped to b.Variable(...)).
    private static string ManifestXmlWithUnmappedSearch() => $$"""
        <BurnManifest xmlns="{{Ns}}">
          <Registration Version="2.0.0" Code="{11111111-2222-3333-4444-555555555555}" Scope="perMachine">
            <Arp DisplayName="WiX App" Publisher="WiX Corp" />
          </Registration>
          <Search xmlns="{{Ns}}" Id="MyRegSearch" Variable="FoundIt" />
        </BurnManifest>
        """;

    /// <summary>
    /// Builds a generator whose native FALKBUNDLE decompiler always fails (so routing
    /// falls back to WiX), and whose WiX decompiler reads the injected manifest.
    /// </summary>
    private static MigrationResult RunWixMock(string manifestXml)
    {
        var native = new BundleDecompiler(
            new MockBundleAccess().WithManifestFailure(ErrorKind.BundleError, "not a FALKBUNDLE (BDC002)."));

        var wixAccess = new MockWixBurnAccess()
            .WithBundleId(TestBundleId)
            .WithManifestXml(manifestXml);
        var wix = new WixBundleDecompiler(wixAccess);

        var generator = new MigrationProjectGenerator(native, wix);
        var result = generator.Generate("legacy.exe", new MigrationOptions(DummySourcePath, ProjectName));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    [Fact]
    public void Generate_WixBundle_PopulatesUnmapped()
    {
        var value = RunWixMock(ManifestXmlWithUnmappedSearch());

        Assert.NotEmpty(value.Unmapped);
        Assert.Contains(value.Unmapped, f => f.Description.Contains("MyRegSearch", StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_WixBundle_ReportListsUnmappedFeatures()
    {
        var value = RunWixMock(ManifestXmlWithUnmappedSearch());
        var report = value.TextFiles["MIGRATION-REPORT.md"];

        Assert.Contains("WiX Burn", report);
        Assert.Contains("MyRegSearch", report);
    }

    [Fact]
    public void Generate_WixBundle_ProgramCsIsRunnable()
    {
        var value = RunWixMock(ManifestXmlWithUnmappedSearch());
        var prog = value.TextFiles["Program.cs"];

        Assert.Contains("return Installer.BuildBundle(args,", prog);
        Assert.Contains("new BundleCompiler().Compile(", prog);
    }
}
