using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class PackageBuilderTests
{
    [Fact]
    public void Build_WithNameAndManufacturer_SetsProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        });

        Assert.Equal("TestApp", package.Name);
        Assert.Equal("TestCorp", package.Manufacturer);
    }

    [Fact]
    public void Build_HasCorrectDefaults()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Equal(InstallScope.PerMachine, package.Scope);
        Assert.Equal(ProcessorArchitecture.X64, package.Architecture);
        Assert.Equal(CompressionLevel.High, package.Compression);
        Assert.Equal(new Version(1, 0, 0), package.Version);
    }

    [Fact]
    public void Files_AddsFileEntries()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("app.exe", package.Files[0].FileName);
    }

    [Fact]
    public void Files_MultipleFiles_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .Add("config.json")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        Assert.Equal(2, package.Files.Count);
        Assert.Equal("app.exe", package.Files[0].FileName);
        Assert.Equal("config.json", package.Files[1].FileName);
    }

    [Fact]
    public void Feature_AddsFeaturesToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Core", f =>
            {
                f.Title = "Core Feature";
                f.IsRequired = true;
            });
        });

        Assert.Single(package.Features);
        Assert.Equal("Core", package.Features[0].Id);
        Assert.Equal("Core Feature", package.Features[0].Title);
        Assert.True(package.Features[0].IsRequired);
    }

    [Fact]
    public void Shortcut_OnDesktop_AddsShortcut()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe").OnDesktop();
        });

        Assert.Single(package.Shortcuts);
        Assert.Equal("My App", package.Shortcuts[0].Name);
        Assert.Equal("app.exe", package.Shortcuts[0].TargetFile);
        Assert.Contains(ShortcutLocation.Desktop, package.Shortcuts[0].Locations);
    }

    [Fact]
    public void Shortcut_OnStartMenu_AddsShortcut()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe").OnStartMenu("MyCompany");
        });

        Assert.Single(package.Shortcuts);
        Assert.Contains(ShortcutLocation.StartMenu, package.Shortcuts[0].Locations);
    }

    [Fact]
    public void Service_AddsServiceToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DisplayName = "My Service";
                svc.StartMode = ServiceStartMode.Automatic;
            });
        });

        Assert.Single(package.Services);
        Assert.Equal("MySvc", package.Services[0].Name);
        Assert.Equal("svc.exe", package.Services[0].Executable);
        Assert.Equal("My Service", package.Services[0].DisplayName);
    }

    [Fact]
    public void Registry_AddsRegistryEntries()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Registry(r => r.Key(RegistryRoot.LocalMachine, @"SOFTWARE\MyApp", k =>
                k.Value("Version", "1.0")));
        });

        Assert.Single(package.RegistryEntries);
        Assert.Equal(RegistryRoot.LocalMachine, package.RegistryEntries[0].Root);
        Assert.Equal(@"SOFTWARE\MyApp", package.RegistryEntries[0].Key);
    }

    [Fact]
    public void EnvironmentVariable_AddsEnvVar()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.EnvironmentVariable("MY_VAR", "my_value");
        });

        Assert.Single(package.EnvironmentVariables);
        Assert.Equal("MY_VAR", package.EnvironmentVariables[0].Name);
        Assert.Equal("my_value", package.EnvironmentVariables[0].Value);
    }

    [Fact]
    public void Property_AddsPropertyToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Property("INSTALL_MODE", "full");
        });

        Assert.Single(package.Properties);
        Assert.Equal("INSTALL_MODE", package.Properties[0].Name);
        Assert.Equal("full", package.Properties[0].Value);
    }

    [Fact]
    public void Require_AddsLaunchCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Require("VersionNT >= 603", "Windows 8.1 or later is required.");
        });

        Assert.Single(package.LaunchConditions);
        Assert.Equal("VersionNT >= 603", package.LaunchConditions[0].Condition);
        Assert.Equal("Windows 8.1 or later is required.", package.LaunchConditions[0].Message);
    }

    [Fact]
    public void Upgrade_ConfiguresUpgradeSettings()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Upgrade(u =>
            {
                u.AllowDowngrades = true;
                u.AllowSameVersion = true;
            });
        });

        Assert.NotNull(package.Upgrade);
        Assert.True(package.Upgrade.AllowDowngrades);
        Assert.True(package.Upgrade.AllowSameVersion);
    }

    [Fact]
    public void Build_GeneratesDeterministicUpgradeCode_FromNameAndManufacturer()
    {
        var package1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });
        var package2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Equal(package1.UpgradeCode, package2.UpgradeCode);
        Assert.NotEqual(Guid.Empty, package1.UpgradeCode);
    }

    [Fact]
    public void Build_DifferentNameOrManufacturer_ProducesDifferentUpgradeCode()
    {
        var package1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App1";
            p.Manufacturer = "Corp";
        });
        var package2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App2";
            p.Manufacturer = "Corp";
        });

        Assert.NotEqual(package1.UpgradeCode, package2.UpgradeCode);
    }

    [Fact]
    public void Build_ExplicitUpgradeCode_UsesProvidedValue()
    {
        var explicitGuid = Guid.NewGuid();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.UpgradeCode = explicitGuid;
        });

        Assert.Equal(explicitGuid, package.UpgradeCode);
    }

    [Fact]
    public void Build_NoFeaturesExplicit_CreatesImplicitCompleteFeature()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Single(package.Features);
        Assert.Equal("Complete", package.Features[0].Id);
        Assert.Equal("Complete", package.Features[0].Title);
        Assert.True(package.Features[0].IsRequired);
        Assert.True(package.Features[0].IsDefault);
    }

    [Fact]
    public void Build_WithExplicitFeatures_DoesNotCreateImplicitFeature()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Custom", f => f.Title = "Custom");
        });

        Assert.Single(package.Features);
        Assert.Equal("Custom", package.Features[0].Id);
    }

    [Fact]
    public void Build_DefaultInstallDirectory_DerivedFromManufacturerAndName()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MyApp";
            p.Manufacturer = "Contoso";
        });

        Assert.NotNull(package.DefaultInstallDirectory);
        var expectedPath = KnownFolder.ProgramFiles / "Contoso" / "MyApp";
        Assert.Equal(expectedPath, package.DefaultInstallDirectory);
    }

    [Fact]
    public void Build_ExplicitInstallDirectory_UsesProvidedValue()
    {
        var customDir = KnownFolder.CommonAppData / "MyApp";
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MyApp";
            p.Manufacturer = "Contoso";
            p.DefaultInstallDirectory = customDir;
        });

        Assert.Equal(customDir, package.DefaultInstallDirectory);
    }

    [Fact]
    public void Build_SetsDescription()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Description = "A test application";
        });

        Assert.Equal("A test application", package.Description);
    }

    [Fact]
    public void Build_ProductCode_IsGenerated()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.NotEqual(Guid.Empty, package.ProductCode);
    }

    [Fact]
    public void Build_ExplicitProductCode_UsesProvidedValue()
    {
        var code = Guid.NewGuid();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ProductCode = code;
        });

        Assert.Equal(code, package.ProductCode);
    }

    [Fact]
    public void Reproducible_ProductCode_IsDeterministicAcrossBuilds()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });

        Assert.Equal(p1.ProductCode, p2.ProductCode);
        Assert.NotEqual(Guid.Empty, p1.ProductCode);
    }

    [Fact]
    public void Reproducible_ProductCode_DiffersForDifferentVersion()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(2, 0, 0);
            p.Reproducible(1708600000L);
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    // Issue #61: ProductCode must default to a deterministic value so a rebuild of the
    // same Name/Manufacturer/Version replaces the previous install instead of installing
    // alongside it (two Add/Remove Programs entries). This is the regression test.
    [Fact]
    public void NonReproducible_ProductCode_IsDeterministicAcrossBuilds()
    {
        var p1 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });
        var p2 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });

        Assert.Equal(p1.ProductCode, p2.ProductCode);
    }

    // Guards against a naive fix that derives ProductCode from Name::Manufacturer alone
    // (dropping Version): Windows Installer major-upgrade rules require a NEW version to
    // get a NEW ProductCode, even without Reproducible() configured.
    [Fact]
    public void NonReproducible_ProductCode_DiffersForDifferentVersion()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(2, 0, 0);
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    // PackageCode is a different code with different MSI rules (identifies the .msi
    // package bytes, not the product) and must stay a fresh Guid per build in normal
    // (non-reproducible) mode -- this fix must not touch it.
    [Fact]
    public void NonReproducible_PackageCode_VariesAcrossBuilds()
    {
        var p1 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });
        var p2 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });

        Assert.NotEqual(p1.PackageCode, p2.PackageCode);
    }

    // Windows Installer only ever reads major.minor.build for ProductVersion (see
    // PropertyTableProducer and UpgradeTableProducer, both call Version.ToString(3)) --
    // the 4th (Revision) field never reaches the compiled MSI at all. A CI build number
    // living in Revision (a completely standard .NET convention, e.g. "1.0.0.100") must
    // therefore NOT change ProductCode, or a rebuild that only bumps Revision gets a new
    // ProductCode while ProductVersion stays identical: RemoveExistingProducts then does
    // nothing (VersionMax is exclusive) and the install lands side by side -- issue #61
    // again, just triggered by a different field.
    [Fact]
    public void NonReproducible_ProductCode_IgnoresRevisionComponent()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0, 7);
        });

        Assert.Equal(p1.ProductCode, p2.ProductCode);
    }

    // Confirms the fix still tracks a real (major/minor/build) version change -- must not
    // regress into deriving ProductCode from Name::Manufacturer alone.
    [Fact]
    public void NonReproducible_ProductCode_DiffersForDifferentBuildComponent()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 1);
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    // A 2-component Version (Build == -1) is reachable today: JsonConfigLoader/
    // StudioBuildService parse "version": "1.0" with plain Version.TryParse (no
    // normalization) and PKG005-007 only bound Major/Minor/Build, not the absence of
    // Build itself. The derivation must clamp the missing Build to 0 instead of calling
    // Version.ToString(3) (which throws ArgumentException below 3 components) -- pins
    // that the clamp doesn't throw and produces the same code as an explicit "1.0.0".
    [Fact]
    public void NonReproducible_ProductCode_TwoComponentVersion_MatchesThreeComponentEquivalent()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
        });

        Assert.Equal(p1.ProductCode, p2.ProductCode);
    }

    // Architecture changes compiled product identity: SummaryInformation's Template
    // (MsiRecipeBuilder.Metadata.cs) and the Component 64-bit attribute bit
    // (ComponentTableProducer.cs) both depend on it. Without Architecture in the key, a
    // normal dual-architecture ship -- same Name/Manufacturer/Version, built once for X86
    // and once for X64 -- derives the SAME ProductCode with different Templates.
    // Installing x86 then x64 returns 1638 (ERROR_PRODUCT_VERSION) instead of installing,
    // with no build-time error.
    [Fact]
    public void NonReproducible_ProductCode_DiffersForDifferentArchitecture()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Architecture = ProcessorArchitecture.X86;
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Architecture = ProcessorArchitecture.X64;
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    // Scope changes compiled product identity: ALLUSERS (PropertyTableProducer.cs) differs
    // between PerMachine and PerUser installs of the same Name/Manufacturer/Version. Without
    // Scope in the key, that collision is the same class of bug as the architecture one
    // above -- a per-machine build and a per-user build of the same product silently share a
    // ProductCode with different ALLUSERS values.
    [Fact]
    public void NonReproducible_ProductCode_DiffersForDifferentScope()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Scope = InstallScope.PerMachine;
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Scope = InstallScope.PerUser;
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    // Pins the coupling nothing else asserts: PackageBuilder's ProductCode derivation must
    // key on the exact same version string PropertyTableProducer.cs writes as
    // ProductVersion (package.Version.ToString(3)). Every other test here compares two
    // builder outputs to each other, so both sides could drift together and stay green. This
    // test recomputes the expected GUID independently, via Version.ToString(3) directly (not
    // PackageBuilder's internal msiVersion field) -- if PropertyTableProducer's normalization
    // ever changes (e.g. to ToString(4)), this is the test that catches it.
    [Fact]
    public void NonReproducible_ProductCode_MatchesPropertyTableProducerVersionString()
    {
        var version = new Version(1, 2, 3);
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = version;
            p.Architecture = ProcessorArchitecture.X64;
            p.Scope = InstallScope.PerMachine;
        });

        var expectedProductCode = GuidUtility.CreateDeterministicGuid(
            GuidUtility.FalkForgeNamespace,
            $"App::Corp::{version.ToString(3)}::x64::machine");

        Assert.Equal(expectedProductCode, package.ProductCode);
    }

    // documentation.html:1168-1169 claims the derivation "applies identically in normal and
    // reproducible-build mode". Every other ProductCode test in this file runs in normal
    // (non-reproducible) mode only -- nothing pinned that claim for reproducible mode until
    // now.
    [Fact]
    public void ProductCode_SameAcrossReproducibleAndNonReproducibleModes()
    {
        var nonReproducible = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
        });
        var reproducible = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });

        Assert.Equal(nonReproducible.ProductCode, reproducible.ProductCode);
    }
}
