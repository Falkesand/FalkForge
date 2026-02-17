using System.Xml.Linq;
using FalkForge;
using FalkForge.Compiler.Bundle;
using Xunit;

namespace FalkForge.Decompiler.Tests;

public sealed class WixManifestMapperTests
{
    private static readonly Guid TestBundleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string MinimalManifest(
        string registrationAttrs = @"Code=""{22222222-2222-2222-2222-222222222222}"" ExecutableName=""setup.exe"" Scope=""perMachine"" Tag="""" Version=""1.0.0"" ProviderKey=""test""",
        string arpAttrs = @"DisplayName=""Test App"" Publisher=""TestCo""",
        string chainContent = @"<MsiPackage Id=""pkg1"" Vital=""yes"" Version=""1.0.0"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />",
        string extraElements = "")
    {
        return $@"<BurnManifest xmlns=""http://schemas.microsoft.com/wix/2008/Burn"">
  <Registration {registrationAttrs}>
    <Arp {arpAttrs} />
  </Registration>
  {extraElements}
  <Chain>
    {chainContent}
  </Chain>
</BurnManifest>";
    }

    private static string V4Manifest(
        string registrationAttrs = @"Code=""{22222222-2222-2222-2222-222222222222}"" ExecutableName=""setup.exe"" PerMachine=""yes"" Tag="""" Version=""1.0.0"" ProviderKey=""test""",
        string arpAttrs = @"DisplayName=""Test App"" Publisher=""TestCo""",
        string chainContent = @"<MsiPackage Id=""pkg1"" Vital=""yes"" Version=""1.0.0"" xmlns=""http://wixtoolset.org/schemas/v4/2008/Burn"" />",
        string extraElements = "")
    {
        return $@"<BurnManifest xmlns=""http://wixtoolset.org/schemas/v4/2008/Burn"">
  <Registration {registrationAttrs}>
    <Arp {arpAttrs} />
  </Registration>
  {extraElements}
  <Chain>
    {chainContent}
  </Chain>
</BurnManifest>";
    }

    [Fact]
    public void Map_Registration_MapsNameManufacturerVersion()
    {
        var xml = XDocument.Parse(MinimalManifest());

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Equal("Test App", model.Name);
        Assert.Equal("TestCo", model.Manufacturer);
        Assert.Equal("1.0.0", model.Version);
    }

    [Fact]
    public void Map_Registration_MapsUpgradeCodeFromCode()
    {
        var xml = XDocument.Parse(MinimalManifest());

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), model.UpgradeCode);
    }

    [Fact]
    public void Map_Registration_MapsScopePerMachine()
    {
        var xml = XDocument.Parse(MinimalManifest(
            registrationAttrs: @"Code=""{22222222-2222-2222-2222-222222222222}"" Scope=""perMachine"" Version=""1.0.0"" ProviderKey=""test"""));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InstallScope.PerMachine, result.Value.Model.Scope);
    }

    [Fact]
    public void Map_Registration_MapsScopePerUser()
    {
        var xml = XDocument.Parse(MinimalManifest(
            registrationAttrs: @"Code=""{22222222-2222-2222-2222-222222222222}"" Scope=""perUser"" Version=""1.0.0"" ProviderKey=""test"""));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InstallScope.PerUser, result.Value.Model.Scope);
    }

    [Fact]
    public void Map_RelatedBundle_MapsRelation()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<RelatedBundle Code=""{33333333-3333-3333-3333-333333333333}"" Action=""Upgrade"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.RelatedBundles);
        Assert.Equal("{33333333-3333-3333-3333-333333333333}", model.RelatedBundles[0].BundleId);
        Assert.Equal(RelatedBundleRelation.Upgrade, model.RelatedBundles[0].Relation);
    }

    [Fact]
    public void Map_MsiPackage_MapsBundlePackage()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<MsiPackage Id=""msi1"" DisplayName=""My MSI"" Version=""2.0.0"" Vital=""yes"" InstallCondition=""VersionNT64"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.Packages);
        var pkg = model.Packages[0];
        Assert.Equal("msi1", pkg.Id);
        Assert.Equal("My MSI", pkg.DisplayName);
        Assert.Equal("2.0.0", pkg.Version);
        Assert.True(pkg.Vital);
        Assert.Equal("VersionNT64", pkg.InstallCondition);
        Assert.Equal(BundlePackageType.MsiPackage, pkg.Type);
    }

    [Fact]
    public void Map_ExePackage_MapsAsExeType()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<ExePackage Id=""exe1"" DisplayName=""My EXE"" Vital=""no"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.ExePackage, model.Packages[0].Type);
        Assert.Equal("exe1", model.Packages[0].Id);
        Assert.False(model.Packages[0].Vital);
    }

    [Fact]
    public void Map_MspPackage_MapsAsMspType()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<MspPackage Id=""msp1"" PatchCode=""{44444444-4444-4444-4444-444444444444}"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MspPackage, model.Packages[0].Type);
        Assert.Equal("{44444444-4444-4444-4444-444444444444}", model.Packages[0].PatchCode);
    }

    [Fact]
    public void Map_MsuPackage_MapsAsMsuType()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<MsuPackage Id=""msu1"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MsuPackage, model.Packages[0].Type);
        Assert.Equal("msu1", model.Packages[0].Id);
    }

    [Fact]
    public void Map_RollbackBoundary_MapsToChainItem()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"
                <RollbackBoundary Id=""rb1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
                <MsiPackage Id=""pkg1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Equal(2, model.Chain.Count);
        var rb = Assert.IsType<RollbackBoundaryChainItem>(model.Chain[0]);
        Assert.Equal("rb1", rb.Boundary.Id);
        Assert.True(rb.Boundary.Vital);
        Assert.IsType<PackageChainItem>(model.Chain[1]);
    }

    [Fact]
    public void Map_Container_MapsContainerModel()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Container Id=""c1"" DownloadUrl=""https://example.com/c1.cab"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Single(model.Containers);
        Assert.Equal("c1", model.Containers[0].Id);
        Assert.Equal("https://example.com/c1.cab", model.Containers[0].DownloadUrl);
    }

    [Fact]
    public void Map_Variables_MapsToModel()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Variable Id=""INSTALLDB"" Value=""false"" Type=""string"" Persisted=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        var variable = Assert.Single(model.Variables);
        Assert.Equal("INSTALLDB", variable.Name);
        Assert.Equal(BundleVariableType.String, variable.Type);
        Assert.Equal("false", variable.DefaultValue);
        Assert.True(variable.Persisted);
        Assert.False(variable.Hidden);
        Assert.False(variable.Secret);
    }

    [Fact]
    public void Map_Variables_NumericType_MapsCorrectly()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Variable Id=""RetryCount"" Value=""3"" Type=""numeric"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        var variable = Assert.Single(model.Variables);
        Assert.Equal(BundleVariableType.Numeric, variable.Type);
    }

    [Fact]
    public void Map_Variables_HiddenFlag_MapsCorrectly()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Variable Id=""SecretKey"" Value=""abc"" Type=""string"" Hidden=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        var variable = Assert.Single(model.Variables);
        Assert.True(variable.Hidden);
    }

    [Fact]
    public void Map_Variables_NotInUnmappedFeatures()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Variable Id=""InstallDir"" Value=""C:\App"" Type=""string"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (_, unmapped) = result.Value;
        Assert.DoesNotContain(unmapped, u => u.Category == "Variable");
    }

    [Fact]
    public void Map_Search_CollectedAsUnmapped()
    {
        var xml = XDocument.Parse(MinimalManifest(
            extraElements: @"<Search Id=""search1"" Variable=""SearchResult"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (_, unmapped) = result.Value;
        var search = Assert.Single(unmapped, u => u.Category == "Search");
        Assert.Contains("Id=search1", search.Description);
        Assert.Contains("Variable=SearchResult", search.Description);
    }

    [Fact]
    public void Map_MsiPackage_WithMsiProperties_MapsToPackageProperties()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<MsiPackage Id=""pkg1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"">
                <MsiProperty Id=""INSTALLFOLDER"" Value=""[ProgramFilesFolder]"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
                <MsiProperty Id=""ALLUSERS"" Value=""1"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
            </MsiPackage>"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        var package = Assert.Single(model.Packages);
        Assert.Equal("[ProgramFilesFolder]", package.Properties["INSTALLFOLDER"]);
        Assert.Equal("1", package.Properties["ALLUSERS"]);
    }

    [Fact]
    public void Map_ExePackage_WithExitCodes_MapsToPackageExitCodes()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<ExePackage Id=""exe1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"">
                <ExitCode Code=""0"" Type=""1"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
                <ExitCode Code=""3010"" Type=""3"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
            </ExePackage>"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        var package = Assert.Single(model.Packages);
        Assert.Equal(ExitCodeBehavior.Success, package.ExitCodes[0]);
        Assert.Equal(ExitCodeBehavior.RebootRequired, package.ExitCodes[3010]);
    }

    [Fact]
    public void Map_MsiPackage_WithMsiProperties_NotInUnmappedFeatures()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<MsiPackage Id=""pkg1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"">
                <MsiProperty Id=""INSTALLFOLDER"" Value=""[ProgramFilesFolder]"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
                <MsiProperty Id=""ALLUSERS"" Value=""1"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
            </MsiPackage>"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (_, unmapped) = result.Value;
        Assert.DoesNotContain(unmapped, u => u.Category == "MsiProperty");
    }

    [Fact]
    public void Map_ExePackage_WithExitCodes_NotInUnmappedFeatures()
    {
        var xml = XDocument.Parse(MinimalManifest(
            chainContent: @"<ExePackage Id=""exe1"" Vital=""yes"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"">
                <ExitCode Code=""0"" Type=""1"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
                <ExitCode Code=""3010"" Type=""3"" xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />
            </ExePackage>"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (_, unmapped) = result.Value;
        Assert.DoesNotContain(unmapped, u => u.Category == "ExitCode");
    }

    [Fact]
    public void Map_EmptyManifest_ReturnsValidDefaults()
    {
        var xml = XDocument.Parse(@"<BurnManifest xmlns=""http://schemas.microsoft.com/wix/2008/Burn"" />");

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, unmapped) = result.Value;
        Assert.Equal("", model.Name);
        Assert.Equal("", model.Manufacturer);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal(TestBundleId, model.BundleId);
        Assert.Equal(Guid.Empty, model.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, model.Scope);
        Assert.Empty(model.Packages);
        Assert.Empty(model.Chain);
        Assert.Empty(model.RelatedBundles);
        Assert.Empty(model.Containers);
        Assert.Empty(unmapped);
    }

    [Fact]
    public void Map_V4Namespace_MapsRegistrationAndChain()
    {
        var xml = XDocument.Parse(V4Manifest(
            registrationAttrs: @"Code=""{22222222-2222-2222-2222-222222222222}"" ExecutableName=""setup.exe"" PerMachine=""yes"" Version=""2.0.0"" ProviderKey=""test""",
            arpAttrs: @"DisplayName=""V4 App"" Publisher=""V4Co""",
            chainContent: @"<MsiPackage Id=""v4pkg"" DisplayName=""V4 MSI"" Vital=""yes"" Version=""2.0.0"" xmlns=""http://wixtoolset.org/schemas/v4/2008/Burn"" />"));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        var (model, _) = result.Value;
        Assert.Equal("V4 App", model.Name);
        Assert.Equal("V4Co", model.Manufacturer);
        Assert.Equal("2.0.0", model.Version);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), model.UpgradeCode);
        Assert.Equal(InstallScope.PerMachine, model.Scope);
        Assert.Single(model.Packages);
        Assert.Equal("v4pkg", model.Packages[0].Id);
        Assert.Equal(BundlePackageType.MsiPackage, model.Packages[0].Type);
    }

    [Fact]
    public void Map_Registration_PerMachineYes_MapsToPerMachineScope()
    {
        var xml = XDocument.Parse(V4Manifest(
            registrationAttrs: @"Code=""{22222222-2222-2222-2222-222222222222}"" PerMachine=""yes"" Version=""1.0.0"" ProviderKey=""test"""));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InstallScope.PerMachine, result.Value.Model.Scope);
    }

    [Fact]
    public void Map_Registration_PerMachineNo_MapsToPerUserScope()
    {
        var xml = XDocument.Parse(V4Manifest(
            registrationAttrs: @"Code=""{22222222-2222-2222-2222-222222222222}"" PerMachine=""no"" Version=""1.0.0"" ProviderKey=""test"""));

        var result = WixManifestMapper.Map(xml, TestBundleId);

        Assert.True(result.IsSuccess);
        Assert.Equal(InstallScope.PerUser, result.Value.Model.Scope);
    }
}
