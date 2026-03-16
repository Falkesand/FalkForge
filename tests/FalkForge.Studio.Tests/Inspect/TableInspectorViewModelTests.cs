using FalkForge.Studio.Editors.TableInspector;
using FalkForge.Studio.Inspect;
using Xunit;

namespace FalkForge.Studio.Tests.Inspect;

public sealed class TableInspectorViewModelTests
{
    [Fact]
    public void InitialState_HasEmptyCollections()
    {
        var vm = new TableInspectorViewModel();

        Assert.Equal(string.Empty, vm.MsiFilePath);
        Assert.Null(vm.SelectedTableName);
        Assert.Null(vm.TableRows);
        Assert.Empty(vm.TableNames);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void LoadMsiFile_NonExistentPath_SetsErrorStatus()
    {
        var vm = new TableInspectorViewModel();

        vm.LoadMsiFile(@"C:\nonexistent\fake.msi");

        Assert.Contains("not found", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.TableNames);
    }

    [Fact]
    public void SelectedTableName_WhenNoFileLoaded_DoesNotThrow()
    {
        var vm = new TableInspectorViewModel();

        vm.SelectedTableName = "Property";

        Assert.Null(vm.TableRows);
    }

    [Fact]
    public void LoadMsiFile_EmptyPath_ResetsState()
    {
        var vm = new TableInspectorViewModel();

        vm.LoadMsiFile("");

        Assert.Equal("Select an MSI file to inspect.", vm.StatusText);
        Assert.Empty(vm.TableNames);
    }

    [Fact]
    public void TableDataChanged_FiredWhenSelectionChanges()
    {
        var vm = new TableInspectorViewModel();
        MsiTableData? receivedData = null;
        var eventFired = false;
        vm.TableDataChanged += (_, data) =>
        {
            eventFired = true;
            receivedData = data;
        };

        // Setting table name without loaded file fires event with null
        vm.SelectedTableName = "SomeTable";

        Assert.True(eventFired);
        Assert.Null(receivedData);
    }

    [Fact]
    public void PropertyChanged_FiredForMsiFilePath()
    {
        var vm = new TableInspectorViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.MsiFilePath = "test.msi";

        Assert.Contains(nameof(TableInspectorViewModel.MsiFilePath), changed);
    }

    [Fact]
    public void PropertyChanged_FiredForSelectedTableName()
    {
        var vm = new TableInspectorViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.SelectedTableName = "Property";

        Assert.Contains(nameof(TableInspectorViewModel.SelectedTableName), changed);
    }
}
