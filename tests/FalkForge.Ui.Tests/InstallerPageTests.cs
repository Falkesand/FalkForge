namespace FalkForge.Ui.Tests;

using System.ComponentModel;
using System.Windows.Controls;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Tests.ViewModels;
using Xunit;

public class TestView : UserControl { }

public class TestPage : InstallerPage<TestView>
{
    public override string Title => "Test Page";
}

public class PropertyTestPage : InstallerPage<TestView>
{
    private string _status = string.Empty;

    public override string Title => "Property Test";

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }
}

public class InstallerPageTests
{
    [Fact]
    public void Title_ReturnsExpectedValue()
    {
        var page = new TestPage();

        Assert.Equal("Test Page", page.Title);
    }

    [Fact]
    public void Engine_SetInternally_ReturnsValue()
    {
        var page = new TestPage();
        var engine = new TestInstallerEngine();

        page.Engine = engine;

        Assert.Same(engine, page.Engine);
    }

    [Fact]
    public void SharedState_SetInternally_ReturnsValue()
    {
        var page = new TestPage();
        var state = new InstallerState();

        page.SharedState = state;

        Assert.Same(state, page.SharedState);
    }

    [Fact]
    public void DetectedState_SetInternally_ReturnsValue()
    {
        var page = new TestPage();

        page.DetectedState = InstallState.Installed;

        Assert.Equal(InstallState.Installed, page.DetectedState);
    }

    [Fact]
    public void OnNext_DefaultBehavior_ReturnsNext()
    {
        var page = new TestPage();

        var result = page.OnNext();

        Assert.Equal(PageResultKind.Next, result.Kind);
    }

    [Fact]
    public void OnBack_DefaultBehavior_ReturnsPrevious()
    {
        var page = new TestPage();

        var result = page.OnBack();

        Assert.Equal(PageResultKind.Previous, result.Kind);
    }

    [Fact]
    public void CanGoNext_DefaultsToTrue()
    {
        var page = new TestPage();

        Assert.True(page.CanGoNext);
    }

    [Fact]
    public void CanGoBack_DefaultsToTrue()
    {
        var page = new TestPage();

        Assert.True(page.CanGoBack);
    }

    [Fact]
    public async Task OnNavigatedToAsync_DefaultBehavior_ReturnsCompletedTask()
    {
        var page = new TestPage();

        var task = page.OnNavigatedToAsync();

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task OnNavigatingFromAsync_DefaultBehavior_ReturnsCompletedTask()
    {
        var page = new TestPage();

        var task = page.OnNavigatingFromAsync();

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public void SetField_RaisesPropertyChanged()
    {
        var page = new PropertyTestPage();
        var changedProperties = new List<string?>();
        page.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        page.Status = "Installing";

        Assert.Single(changedProperties);
        Assert.Equal(nameof(PropertyTestPage.Status), changedProperties[0]);
    }

    [Fact]
    public void SetField_SameValue_DoesNotRaisePropertyChanged()
    {
        var page = new PropertyTestPage { Status = "Done" };
        var changedProperties = new List<string?>();
        page.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        page.Status = "Done";

        Assert.Empty(changedProperties);
    }

    [WpfFact]
    public void CreateViewInternal_ReturnsCorrectViewType()
    {
        var page = new TestPage();

        var view = page.CreateViewInternal();

        Assert.IsType<TestView>(view);
    }

    [WpfFact]
    public void CreateViewInternal_SetsDataContextToPage()
    {
        var page = new TestPage();

        var view = page.CreateViewInternal();

        Assert.Same(page, view.DataContext);
    }
}
