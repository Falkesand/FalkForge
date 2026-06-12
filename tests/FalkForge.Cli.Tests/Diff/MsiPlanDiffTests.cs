using FalkForge.Cli.Diff;
using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Cli.Tests.Diff;

/// <summary>
/// Unit tests for <see cref="MsiPlanDiff"/>. All fixtures are constructed in-memory
/// — no MSI files required. Tests verify that the diff engine correctly classifies
/// added, removed, and changed items across each diff dimension.
/// </summary>
public sealed class MsiPlanDiffTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static MsiReadRecipe EmptyRecipe() => new()
    {
        Properties        = [],
        Directories       = [],
        Components        = [],
        Files             = [],
        Features          = [],
        FeatureComponents = [],
        RegistryEntries   = [],
        Services          = [],
        Shortcuts         = [],
        Upgrades          = [],
    };

    private static PropertyRow Prop(string name, string value) => new(name, value);

    private static FileRow File(string id, string name, int size, string? version = null) =>
        new(id, "Component1", name, size, version, null, 0, 1);

    private static ServiceRow Service(string id, string name, string? display = null) =>
        new(id, name, display, 16, 2, 1, null, null, null, null, null, "Component1", null);

    private static RegistryRow Reg(string id, int root, string key, string? name, string? value) =>
        new(id, root, key, name, value, "Component1");

    private static FeatureRow Feature(string id, string title, int level = 1, string? parent = null) =>
        new(id, parent, title, null, 0, level, null, 0);

    private static ShortcutRow Shortcut(string id, string name, string target) =>
        new(id, "APPLICATIONSFOLDER", name, "Component1", target, null, null, null, null, null, null, null);

    private static UpgradeRow Upgrade(string code, string? min, string? max) =>
        new(code, min, max, null, 256, null, "UPGRADEFOUND");

    // -------------------------------------------------------------------------
    // Baseline: identical recipes produce no changes
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_IdenticalRecipes_NoChanges()
    {
        var recipe = EmptyRecipe() with
        {
            Services = [Service("SvcA", "MyService", "My Service")],
            RegistryEntries = [Reg("R1", 2, @"SOFTWARE\Test", "Version", "1.0")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", recipe, recipe);

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.TotalChanges);
        Assert.Equal("msi", result.Mode);
    }

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_ProductVersion_Changed()
    {
        var oldRecipe = EmptyRecipe() with
        {
            Properties = [Prop("ProductVersion", "1.0.0"), Prop("ProductName", "MyApp")],
        };
        var newRecipe = EmptyRecipe() with
        {
            Properties = [Prop("ProductVersion", "2.0.0"), Prop("ProductName", "MyApp")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        Assert.True(result.HasChanges);
        var propsSection = result.Sections.Single(s => s.Title == "Properties");
        Assert.Equal(1, propsSection.ChangeCount);
        var versionItem = propsSection.Items.Single(i => i.Label == "ProductVersion");
        Assert.Equal(DiffStatus.Changed, versionItem.Status);
        Assert.Equal("1.0.0", versionItem.OldValue);
        Assert.Equal("2.0.0", versionItem.NewValue);
    }

    [Fact]
    public void Diff_TrackedProperty_Added()
    {
        var oldRecipe = EmptyRecipe() with
        {
            Properties = [Prop("ProductName", "MyApp")],
        };
        var newRecipe = EmptyRecipe() with
        {
            Properties = [Prop("ProductName", "MyApp"), Prop("Manufacturer", "Contoso")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var propsSection = result.Sections.Single(s => s.Title == "Properties");
        var mfgItem = propsSection.Items.Single(i => i.Label == "Manufacturer");
        Assert.Equal(DiffStatus.Added, mfgItem.Status);
        Assert.Null(mfgItem.OldValue);
        Assert.Equal("Contoso", mfgItem.NewValue);
    }

    [Fact]
    public void Diff_UntrackedProperty_NotIncluded()
    {
        // "INTERNALPROP" is not in the tracked-property set and should be invisible.
        var oldRecipe = EmptyRecipe() with
        {
            Properties = [Prop("INTERNALPROP", "xyz")],
        };
        var newRecipe = EmptyRecipe() with
        {
            Properties = [Prop("INTERNALPROP", "abc")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        Assert.False(result.HasChanges);
    }

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Service_Added()
    {
        var oldRecipe = EmptyRecipe();
        var newRecipe = EmptyRecipe() with
        {
            Services = [Service("SvcA", "TargetService", "Target Service Display")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var svcSection = result.Sections.Single(s => s.Title == "Services");
        Assert.Equal(1, svcSection.ChangeCount);
        var item = svcSection.Items.Single(i => i.Label == "TargetService");
        Assert.Equal(DiffStatus.Added, item.Status);
        Assert.Null(item.OldValue);
        Assert.Contains("Target Service Display", item.NewValue!);
    }

    [Fact]
    public void Diff_Service_Removed()
    {
        var oldRecipe = EmptyRecipe() with
        {
            Services = [Service("SvcA", "OldService")],
        };
        var newRecipe = EmptyRecipe();

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var svcSection = result.Sections.Single(s => s.Title == "Services");
        var item = svcSection.Items.Single(i => i.Label == "OldService");
        Assert.Equal(DiffStatus.Removed, item.Status);
    }

    [Fact]
    public void Diff_Service_StartTypeChanged()
    {
        // Old: StartType=2 (Automatic), New: StartType=3 (Manual)
        var old = EmptyRecipe() with
        {
            Services = [new ServiceRow("Svc1", "Worker", "Worker Svc", 16, 2, 1, null, null, null, null, null, "Comp1", null)],
        };
        var @new = EmptyRecipe() with
        {
            Services = [new ServiceRow("Svc1", "Worker", "Worker Svc", 16, 3, 1, null, null, null, null, null, "Comp1", null)],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", old, @new);

        var svcSection = result.Sections.Single(s => s.Title == "Services");
        Assert.Equal(1, svcSection.ChangeCount);
        var item = svcSection.Items.Single(i => i.Label == "Worker");
        Assert.Equal(DiffStatus.Changed, item.Status);
        Assert.Contains("start=2", item.OldValue!);
        Assert.Contains("start=3", item.NewValue!);
    }

    // -------------------------------------------------------------------------
    // Registry entries
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_RegistryEntry_Added()
    {
        var oldRecipe = EmptyRecipe();
        var newRecipe = EmptyRecipe() with
        {
            RegistryEntries = [Reg("R1", 2, @"SOFTWARE\MyApp", "InstallDir", @"C:\MyApp")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var regSection = result.Sections.Single(s => s.Title == "Registry Entries");
        Assert.Equal(1, regSection.ChangeCount);
        var item = regSection.Items.Single();
        Assert.Equal(DiffStatus.Added, item.Status);
        Assert.Equal(@"HKLM\SOFTWARE\MyApp\InstallDir", item.Label);
    }

    [Fact]
    public void Diff_RegistryEntry_ValueChanged()
    {
        var oldRecipe = EmptyRecipe() with
        {
            RegistryEntries = [Reg("R1", 2, @"SOFTWARE\MyApp", "Version", "1.0")],
        };
        var newRecipe = EmptyRecipe() with
        {
            RegistryEntries = [Reg("R1", 2, @"SOFTWARE\MyApp", "Version", "2.0")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var regSection = result.Sections.Single(s => s.Title == "Registry Entries");
        var item = regSection.Items.Single(i => i.Status == DiffStatus.Changed);
        Assert.Equal("1.0", item.OldValue);
        Assert.Equal("2.0", item.NewValue);
    }

    [Fact]
    public void Diff_RegistryRoot_Mapped()
    {
        // Root 0=HKCR, 1=HKCU, 2=HKLM, 3=HKU
        var recipe = EmptyRecipe() with
        {
            RegistryEntries =
            [
                Reg("R0", 0, @"Software\Classes\myext", null, "MyApp"),
                Reg("R1", 1, @"Software\MyApp", "Setting", "1"),
                Reg("R2", 2, @"SOFTWARE\MyApp", "Version", "1.0"),
                Reg("R3", 3, @"S-1-5-21\MyApp", "Profile", "X"),
            ],
        };

        // Diff against empty — all four are Added
        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), recipe);

        var regSection = result.Sections.Single(s => s.Title == "Registry Entries");
        var labels = regSection.Items.Select(i => i.Label).ToList();

        Assert.Contains(@"HKCR\Software\Classes\myext", labels);
        Assert.Contains(@"HKCU\Software\MyApp\Setting", labels);
        Assert.Contains(@"HKLM\SOFTWARE\MyApp\Version", labels);
        Assert.Contains(@"HKU\S-1-5-21\MyApp\Profile", labels);
    }

    // -------------------------------------------------------------------------
    // Files
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_File_Added()
    {
        var oldRecipe = EmptyRecipe();
        var newRecipe = EmptyRecipe() with
        {
            Files = [File("File1", "App.exe", 1024, "2.0.0.0")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var fileSection = result.Sections.Single(s => s.Title == "Files");
        Assert.Equal(1, fileSection.ChangeCount);
        var item = fileSection.Items.Single();
        Assert.Equal(DiffStatus.Added, item.Status);
        Assert.Contains("App.exe", item.Label);
    }

    [Fact]
    public void Diff_File_SizeChanged()
    {
        var oldRecipe = EmptyRecipe() with
        {
            Files = [File("File1", "App.exe", 1024, "1.0.0.0")],
        };
        var newRecipe = EmptyRecipe() with
        {
            Files = [File("File1", "App.exe", 2048, "2.0.0.0")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var fileSection = result.Sections.Single(s => s.Title == "Files");
        var item = fileSection.Items.Single(i => i.Status == DiffStatus.Changed);
        Assert.Contains("1024", item.OldValue!);
        Assert.Contains("2048", item.NewValue!);
    }

    [Fact]
    public void Diff_File_LongNameExtracted()
    {
        // MSI FileName column "8DOT3NM|LongFileName.exe" — label must show long name
        var newRecipe = EmptyRecipe() with
        {
            Files = [File("F1", "APP_EXE|Application.exe", 512)],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), newRecipe);

        var fileSection = result.Sections.Single(s => s.Title == "Files");
        Assert.Equal("Application.exe", fileSection.Items.Single().Label);
    }

    // -------------------------------------------------------------------------
    // Features
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Feature_Added()
    {
        var newRecipe = EmptyRecipe() with
        {
            Features = [Feature("CoreFeature", "Core Components")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), newRecipe);

        var featSection = result.Sections.Single(s => s.Title == "Features");
        var item = featSection.Items.Single(i => i.Label == "CoreFeature");
        Assert.Equal(DiffStatus.Added, item.Status);
    }

    [Fact]
    public void Diff_Feature_LevelChanged()
    {
        var oldRecipe = EmptyRecipe() with
        {
            Features = [Feature("Optional", "Optional Feature", level: 100)],
        };
        var newRecipe = EmptyRecipe() with
        {
            Features = [Feature("Optional", "Optional Feature", level: 200)],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", oldRecipe, newRecipe);

        var featSection = result.Sections.Single(s => s.Title == "Features");
        var item = featSection.Items.Single(i => i.Status == DiffStatus.Changed);
        Assert.Contains("level=100", item.OldValue!);
        Assert.Contains("level=200", item.NewValue!);
    }

    // -------------------------------------------------------------------------
    // Shortcuts
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Shortcut_Added()
    {
        var newRecipe = EmptyRecipe() with
        {
            Shortcuts = [Shortcut("SC1", "MyApp|My Application", "[APPLICATIONSFOLDER]App.exe")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), newRecipe);

        var scSection = result.Sections.Single(s => s.Title == "Shortcuts");
        var item = scSection.Items.Single();
        Assert.Equal(DiffStatus.Added, item.Status);
        Assert.Equal("My Application", item.Label); // long name extracted
    }

    // -------------------------------------------------------------------------
    // Upgrades
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_UpgradeEntry_Added()
    {
        var code = "{12345678-1234-1234-1234-123456789ABC}";
        var newRecipe = EmptyRecipe() with
        {
            Upgrades = [Upgrade(code, "1.0.0", "2.0.0")],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), newRecipe);

        var upSection = result.Sections.Single(s => s.Title == "Upgrade Entries");
        Assert.Equal(1, upSection.ChangeCount);
        Assert.Equal(DiffStatus.Added, upSection.Items.Single().Status);
    }

    // -------------------------------------------------------------------------
    // Output metadata
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Paths_PreservedInResult()
    {
        var result = MsiPlanDiff.Diff("a.msi", "b.msi", EmptyRecipe(), EmptyRecipe());
        Assert.Equal("a.msi", result.OldPath);
        Assert.Equal("b.msi", result.NewPath);
        Assert.Equal("msi", result.Mode);
    }

    [Fact]
    public void Diff_EmptySections_ExcludedFromResult()
    {
        // Identical recipes should produce no sections at all (all empty = excluded).
        var result = MsiPlanDiff.Diff("a.msi", "b.msi", EmptyRecipe(), EmptyRecipe());
        Assert.Empty(result.Sections);
    }

    [Fact]
    public void Diff_TotalChanges_IsCountOfAllNonUnchanged()
    {
        var old = EmptyRecipe() with
        {
            Services = [Service("S1", "Svc1")],
            RegistryEntries = [Reg("R1", 2, @"SW\X", "V", "1")],
        };
        var @new = EmptyRecipe() with
        {
            Services = [Service("S2", "Svc2")],                   // S1 removed, S2 added = 2
            RegistryEntries = [Reg("R1", 2, @"SW\X", "V", "2")],  // R1 changed = 1
        };

        var result = MsiPlanDiff.Diff("a.msi", "b.msi", old, @new);

        Assert.Equal(3, result.TotalChanges);
    }

    // -------------------------------------------------------------------------
    // Deterministic ordering
    // -------------------------------------------------------------------------
    [Fact]
    public void Diff_Services_OrderedAlphabetically()
    {
        var recipe = EmptyRecipe() with
        {
            Services =
            [
                Service("S3", "ZService"),
                Service("S1", "AService"),
                Service("S2", "MService"),
            ],
        };

        var result = MsiPlanDiff.Diff("old.msi", "new.msi", EmptyRecipe(), recipe);

        var svcSection = result.Sections.Single(s => s.Title == "Services");
        var labels = svcSection.Items.Select(i => i.Label).ToList();
        Assert.Equal(["AService", "MService", "ZService"], labels);
    }
}
