using FalkForge.Studio.Editors.BundleChain;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Editors;

public class BundleChainViewModelTests
{
    private static StudioProject CreateBundleProject(params BundlePackageSection[] packages)
    {
        var project = StudioProjectLoader.NewProject();
        project.ProjectType = "bundle";
        project.BundleSettings = new BundleSettingsSection { Name = "Test", Manufacturer = "Corp" };
        foreach (var pkg in packages)
            project.BundlePackages.Add(pkg);
        return project;
    }

    [Fact]
    public void Constructor_LoadsPackagesFromProject()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Server", Type = "MsiPackage" },
            new BundlePackageSection { DisplayName = "Client", Type = "ExePackage" });

        var vm = new BundleChainViewModel(project);

        Assert.Equal(2, vm.ChainItems.Count);
        Assert.Equal("Server", vm.ChainItems[0].Name);
        Assert.Equal("MsiPackage", vm.ChainItems[0].PackageType);
        Assert.Equal("Client", vm.ChainItems[1].Name);
        Assert.Equal("ExePackage", vm.ChainItems[1].PackageType);
    }

    [Fact]
    public void Constructor_EmptyBundlePackages_EmptyChain()
    {
        var project = CreateBundleProject();

        var vm = new BundleChainViewModel(project);

        Assert.Empty(vm.ChainItems);
    }

    [Fact]
    public void MoveUp_ReordersItems()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "First", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Second", SourcePath = "b.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[1];

        vm.MoveUp();

        Assert.Equal("Second", vm.ChainItems[0].Name);
        Assert.Equal("First", vm.ChainItems[1].Name);
    }

    [Fact]
    public void MoveUp_AtIndexZero_DoesNothing()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "First", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Second", SourcePath = "b.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[0];

        vm.MoveUp();

        Assert.Equal("First", vm.ChainItems[0].Name);
        Assert.Equal("Second", vm.ChainItems[1].Name);
    }

    [Fact]
    public void MoveDown_ReordersItems()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "First", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Second", SourcePath = "b.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[0];

        vm.MoveDown();

        Assert.Equal("Second", vm.ChainItems[0].Name);
        Assert.Equal("First", vm.ChainItems[1].Name);
    }

    [Fact]
    public void MoveDown_AtLastIndex_DoesNothing()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "First", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Second", SourcePath = "b.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[1];

        vm.MoveDown();

        Assert.Equal("First", vm.ChainItems[0].Name);
        Assert.Equal("Second", vm.ChainItems[1].Name);
    }

    [Fact]
    public void AddRollbackBoundary_InsertsBoundaryItem()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[0];

        vm.AddRollbackBoundary();

        Assert.Equal(2, vm.ChainItems.Count);
        Assert.True(vm.ChainItems[1].IsRollbackBoundary);
        Assert.Equal("Rollback Boundary", vm.ChainItems[1].Name);
    }

    [Fact]
    public void AddRollbackBoundary_NoSelection_AppendsToEnd()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = null;

        vm.AddRollbackBoundary();

        Assert.Equal(2, vm.ChainItems.Count);
        Assert.True(vm.ChainItems[1].IsRollbackBoundary);
    }

    [Fact]
    public void RemoveItem_RemovesSelectedPackage()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "Pkg2", SourcePath = "b.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[0];

        vm.RemoveItem();

        Assert.Single(vm.ChainItems);
        Assert.Equal("Pkg2", vm.ChainItems[0].Name);
        Assert.Single(project.BundlePackages);
    }

    [Fact]
    public void RemoveItem_NullSelection_DoesNothing()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = null;

        vm.RemoveItem();

        Assert.Single(vm.ChainItems);
    }

    [Fact]
    public void SyncBackToProject_AfterReorder()
    {
        var pkg1 = new BundlePackageSection { DisplayName = "First", SourcePath = "a.msi" };
        var pkg2 = new BundlePackageSection { DisplayName = "Second", SourcePath = "b.msi" };
        var project = CreateBundleProject(pkg1, pkg2);
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[1];

        vm.MoveUp();

        Assert.Equal("Second", project.BundlePackages[0].DisplayName);
        Assert.Equal("First", project.BundlePackages[1].DisplayName);
    }

    [Fact]
    public void DisplayOrders_UpdateAfterMoveUp()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "A", SourcePath = "a.msi" },
            new BundlePackageSection { DisplayName = "B", SourcePath = "b.msi" },
            new BundlePackageSection { DisplayName = "C", SourcePath = "c.msi" });
        var vm = new BundleChainViewModel(project);
        vm.SelectedItem = vm.ChainItems[2];

        vm.MoveUp();

        Assert.Equal(0, vm.ChainItems[0].DisplayOrder);
        Assert.Equal(1, vm.ChainItems[1].DisplayOrder);
        Assert.Equal(2, vm.ChainItems[2].DisplayOrder);
    }

    [Fact]
    public void ChainItemViewModel_PropertyChange_SyncsToModel()
    {
        var model = new BundlePackageSection { DisplayName = "Test", Type = "MsiPackage", Vital = true };
        var item = new ChainItemViewModel(model);

        item.Name = "Updated";
        Assert.Equal("Updated", model.DisplayName);

        item.PackageType = "ExePackage";
        Assert.Equal("ExePackage", model.Type);

        item.InstallCondition = "VersionNT >= 603";
        Assert.Equal("VersionNT >= 603", model.InstallCondition);

        item.Vital = false;
        Assert.False(model.Vital);
    }

    [Fact]
    public void RemoveRollbackBoundary_DoesNotAffectProjectPackages()
    {
        var project = CreateBundleProject(
            new BundlePackageSection { DisplayName = "Pkg1", SourcePath = "a.msi" });
        var vm = new BundleChainViewModel(project);
        vm.AddRollbackBoundary();
        vm.SelectedItem = vm.ChainItems[1]; // the rollback boundary

        vm.RemoveItem();

        Assert.Single(vm.ChainItems);
        Assert.Single(project.BundlePackages);
        Assert.Equal("Pkg1", vm.ChainItems[0].Name);
    }
}
