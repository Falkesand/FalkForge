using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Regression coverage for feature-tree reconstruction (<see cref="MsiPackageReconstructor.Rebuild"/>).
/// Pins the Level/Attributes encoding <see cref="FalkForge.Compiler.Msi.Recipe.Producers.FeatureTableProducer"/>
/// actually writes: required features get Level=1/Attributes=16, optional-default features get
/// Level=1/Attributes=0, and non-installed features get Level=1000. There is no Level=0 case, so a
/// decompiler that reads Level==0 to mean "required" never fires — and Level&gt;=1 to mean "default"
/// wrongly includes the Level=1000 (not-installed) row too.
/// </summary>
public sealed class MsiPackageReconstructorFeaturesTests
{
    private static IReadOnlyList<FalkForge.Models.FeatureModel> Reconstruct(params FeatureRow[] featureRows)
        => MsiPackageReconstructor.Rebuild(
            propertyRows: [],
            directoryRows: [],
            componentRows: [],
            fileRows: [],
            featureRows: featureRows,
            featureComponentsRows: [],
            registryRows: [],
            serviceRows: [],
            shortcutRows: [],
            upgradeRows: []).Features;

    [Fact]
    public void Rebuild_RequiredFeature_Level1Attributes16_IsRequiredAndDefault()
    {
        var features = Reconstruct(
            new FeatureRow("Required", null, "Required Feature", null, Display: 1, Level: 1, Directory_: "INSTALLDIR", Attributes: 16));

        var feature = Assert.Single(features);
        Assert.True(feature.IsRequired);
        Assert.True(feature.IsDefault);
    }

    [Fact]
    public void Rebuild_OptionalDefaultFeature_Level1Attributes0_IsDefaultNotRequired()
    {
        var features = Reconstruct(
            new FeatureRow("Optional", null, "Optional Feature", null, Display: 1, Level: 1, Directory_: "INSTALLDIR", Attributes: 0));

        var feature = Assert.Single(features);
        Assert.False(feature.IsRequired);
        Assert.True(feature.IsDefault);
    }

    [Fact]
    public void Rebuild_NotInstalledFeature_Level1000_IsNeitherRequiredNorDefault()
    {
        var features = Reconstruct(
            new FeatureRow("Hidden", null, "Hidden Feature", null, Display: 2, Level: 1000, Directory_: "INSTALLDIR", Attributes: 0));

        var feature = Assert.Single(features);
        Assert.False(feature.IsRequired);
        Assert.False(feature.IsDefault);
    }
}
