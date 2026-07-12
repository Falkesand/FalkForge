using FalkForge.Builders;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Shortcuts, environment variables, fonts, INI files, permissions, and file associations added
/// via FeatureBuilder.Shortcut()/.EnvironmentVariable()/.Font()/.IniFile()/.Permission()/
/// .FileAssociation() must survive the PackageBuilder.Feature() call and appear in the
/// corresponding PackageModel list with the correct FeatureRef, so the compiler can gate their
/// compiled MSI placement to the right feature. Mirrors FeatureBuilderServiceRegistryTests, which
/// proves the same contract for services and registry entries.
/// </summary>
public sealed class FeatureBuilderMoreModelsTests
{
    [Fact]
    public void Feature_AddShortcut_ScopedShortcutHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Shortcut("App", "app.exe").OnDesktop();
            });
        });

        var shortcut = Assert.Single(package.Shortcuts);
        Assert.Equal("ServerFeature", shortcut.FeatureRef);
    }

    [Fact]
    public void Feature_AddEnvironmentVariable_ScopedEntryHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.EnvironmentVariable("APP_HOME", @"C:\payload");
            });
        });

        var envVar = Assert.Single(package.EnvironmentVariables);
        Assert.Equal("ServerFeature", envVar.FeatureRef);
    }

    [Fact]
    public void Feature_AddFont_ScopedEntryHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Font("demofont.ttf");
            });
        });

        var font = Assert.Single(package.Fonts);
        Assert.Equal("ServerFeature", font.FeatureRef);
    }

    [Fact]
    public void Feature_AddIniFile_ScopedEntryHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.IniFile("app.ini", ini => ini.Section("Settings").Key("Debug").Value("0"));
            });
        });

        var ini = Assert.Single(package.IniFiles);
        Assert.Equal("ServerFeature", ini.FeatureRef);
    }

    [Fact]
    public void Feature_AddPermission_ScopedEntryHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Permission("DataFolder", perm =>
                {
                    perm.Sddl = "D:(A;;FA;;;BA)";
                    perm.ForTable("CreateFolder");
                });
            });
        });

        var perm = Assert.Single(package.Permissions);
        Assert.Equal("ServerFeature", perm.FeatureRef);
    }

    [Fact]
    public void Feature_AddFileAssociation_ScopedEntryHasCorrectFeatureRef()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.FileAssociation(".foo", a => a.ProgId("App.Foo"));
            });
        });

        var assoc = Assert.Single(package.FileAssociations);
        Assert.Equal("ServerFeature", assoc.FeatureRef);
    }

    [Fact]
    public void Feature_AddAllSix_MultipleFeatures_EachGetsCorrectRef()
    {
        // WHY: when two features each declare one of the six entry types, each must carry its own
        // feature's ref, not bleed into the other feature or the default "Complete" feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("FeatureA", f =>
            {
                f.Shortcut("AppA", "a.exe").OnDesktop();
                f.EnvironmentVariable("VAR_A", "a");
                f.Font("fontA.ttf");
                f.IniFile("a.ini", ini => ini.Section("S").Key("K").Value("a"));
                f.Permission("LockA", perm => { perm.Sddl = "D:(A;;FA;;;BA)"; });
                f.FileAssociation(".a", a => a.ProgId("App.A"));
            });
            p.Feature("FeatureB", f =>
            {
                f.Shortcut("AppB", "b.exe").OnDesktop();
                f.EnvironmentVariable("VAR_B", "b");
                f.Font("fontB.ttf");
                f.IniFile("b.ini", ini => ini.Section("S").Key("K").Value("b"));
                f.Permission("LockB", perm => { perm.Sddl = "D:(A;;FA;;;BA)"; });
                f.FileAssociation(".b", a => a.ProgId("App.B"));
            });
        });

        Assert.Equal("FeatureA", Assert.Single(package.Shortcuts, s => s.Name == "AppA").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.Shortcuts, s => s.Name == "AppB").FeatureRef);

        Assert.Equal("FeatureA", Assert.Single(package.EnvironmentVariables, e => e.Name == "VAR_A").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.EnvironmentVariables, e => e.Name == "VAR_B").FeatureRef);

        Assert.Equal("FeatureA", Assert.Single(package.Fonts, fnt => fnt.FileName == "fontA.ttf").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.Fonts, fnt => fnt.FileName == "fontB.ttf").FeatureRef);

        Assert.Equal("FeatureA", Assert.Single(package.IniFiles, i => i.FileName == "a.ini").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.IniFiles, i => i.FileName == "b.ini").FeatureRef);

        Assert.Equal("FeatureA", Assert.Single(package.Permissions, pm => pm.LockObject == "LockA").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.Permissions, pm => pm.LockObject == "LockB").FeatureRef);

        Assert.Equal("FeatureA", Assert.Single(package.FileAssociations, a => a.Extension == ".a").FeatureRef);
        Assert.Equal("FeatureB", Assert.Single(package.FileAssociations, a => a.Extension == ".b").FeatureRef);
    }
}
