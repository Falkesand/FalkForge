using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Regression coverage for downgrade-guard reconstruction (<see cref="MsiPackageReconstructor.Rebuild"/>).
/// <see cref="FalkForge.Compiler.Msi.Recipe.Producers.UpgradeTableProducer"/> writes the
/// <c>NEWERVERSIONFOUND</c> / <c>msidbUpgradeAttributesOnlyDetect</c> downgrade-detection row with the
/// current version in <c>VersionMin</c> and an EMPTY <c>VersionMax</c> — so a decompiler that keys the
/// "downgrade blocked" heuristic off a non-empty <c>VersionMax</c> never detects it, and silently
/// reconstructs <c>AllowDowngrades = true</c> even when a launch condition for
/// <c>NOT NEWERVERSIONFOUND</c> is authored.
/// </summary>
public sealed class MsiPackageReconstructorUpgradeTests
{
    private const string UpgradeCode = "{11111111-1111-1111-1111-111111111111}";
    private const int MsidbUpgradeAttributesOnlyDetect = 0x00000002;

    private static FalkForge.Models.DowngradeModel? Reconstruct(params UpgradeRow[] upgradeRows)
        => MsiPackageReconstructor.Rebuild(
            propertyRows: [],
            directoryRows: [],
            componentRows: [],
            fileRows: [],
            featureRows: [],
            featureComponentsRows: [],
            registryRows: [],
            serviceRows: [],
            shortcutRows: [],
            upgradeRows: upgradeRows).Downgrade;

    [Fact]
    public void Rebuild_NewerVersionOnlyDetectRow_VersionMinCarriesVersion_BlocksDowngrades()
    {
        // Mirrors UpgradeTableProducer.MakeRow(upgradeCode, versionStr, string.Empty, 2, "NEWERVERSIONFOUND"):
        // VersionMin = current version, VersionMax = empty.
        var row = new UpgradeRow(UpgradeCode, VersionMin: "1.2.3", VersionMax: "", Language: null,
            Attributes: MsidbUpgradeAttributesOnlyDetect, Remove: null, ActionProperty: "NEWERVERSIONFOUND");

        var downgrade = Reconstruct(row);

        Assert.NotNull(downgrade);
        Assert.False(downgrade.AllowDowngrades);
    }

    [Fact]
    public void Rebuild_NoOnlyDetectRow_AllowsDowngrades()
    {
        // OLDERVERSIONFOUND row only (no msidbUpgradeAttributesOnlyDetect bit) — downgrades are allowed.
        var row = new UpgradeRow(UpgradeCode, VersionMin: "0.0.0", VersionMax: "1.2.3", Language: null,
            Attributes: 256, Remove: null, ActionProperty: "OLDERVERSIONFOUND");

        var downgrade = Reconstruct(row);

        Assert.NotNull(downgrade);
        Assert.True(downgrade.AllowDowngrades);
    }
}
