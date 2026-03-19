namespace FalkForge.Ui.Tests;

using System.ComponentModel;
using System.Windows.Controls;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Localization;
using FalkForge.Ui.Tests.ViewModels;
using Xunit;

public class TestView : UserControl { }

public class TestPage : InstallerPage<TestView>
{
    public override string Title => "Test Page";

    public string TestLocalize(string key) => Localize(key);
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

    private bool _flag;

    public bool Flag
    {
        get => _flag;
        set => SetField(ref _flag, value, [nameof(NotFlag)]);
    }

    public bool NotFlag => !_flag;

    private int _count;

    public int Count
    {
        get => _count;
        set => SetField(ref _count, value, [nameof(IsPositive), nameof(IsEven)]);
    }

    public bool IsPositive => _count > 0;
    public bool IsEven => _count % 2 == 0;
}

public class PasswordTestPage : InstallerPage<TestView>
{
    public override string Title => "Password Test";

    public SensitiveBytes ReadPassword(string key) => GetPassword(key);
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

    [Fact]
    public void SetField_AlsoNotify_RaisesAllPropertyChanged()
    {
        var page = new PropertyTestPage();
        var changed = new List<string?>();
        page.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        page.Flag = true;

        Assert.Equal(2, changed.Count);
        Assert.Equal(nameof(PropertyTestPage.Flag), changed[0]);
        Assert.Equal(nameof(PropertyTestPage.NotFlag), changed[1]);
    }

    [Fact]
    public void SetField_AlsoNotify_SameValue_DoesNotRaise()
    {
        var page = new PropertyTestPage();
        var changed = new List<string?>();
        page.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        page.Flag = false;

        Assert.Empty(changed);
    }

    [Fact]
    public void SetField_AlsoNotify_MultipleDependents_RaisesInOrder()
    {
        var page = new PropertyTestPage();
        var changed = new List<string?>();
        page.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        page.Count = 3;

        Assert.Equal(3, changed.Count);
        Assert.Equal(nameof(PropertyTestPage.Count), changed[0]);
        Assert.Equal(nameof(PropertyTestPage.IsPositive), changed[1]);
        Assert.Equal(nameof(PropertyTestPage.IsEven), changed[2]);
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

    [Fact]
    public void GetPassword_unregistered_key_returns_empty()
    {
        var page = new PasswordTestPage();

        using var result = page.ReadPassword("Missing");

        Assert.True(result.IsEmpty);
    }

    [WpfFact]
    public void GetPassword_registered_passwordbox_returns_bytes()
    {
        var page = new PasswordTestPage();
        var box = new System.Windows.Controls.PasswordBox();
        box.Password = "secret";
        page.RegisterPasswordBox("Test", box);

        using var result = page.ReadPassword("Test");

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("secret"), result.Span.ToArray());
    }

    [WpfFact]
    public void UnregisterPasswordBox_removes_registration()
    {
        var page = new PasswordTestPage();
        var box = new System.Windows.Controls.PasswordBox();
        box.Password = "secret";
        page.RegisterPasswordBox("Test", box);

        page.UnregisterPasswordBox("Test");
        using var result = page.ReadPassword("Test");

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Localize_without_resolver_returns_key()
    {
        var page = new TestPage();
        Assert.Equal("Some.Key", page.TestLocalize("Some.Key"));
    }

    [Fact]
    public void Localize_with_resolver_returns_resolved_string()
    {
        var page = new TestPage();
        var cultures = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["Test.Title"] = "Hello" }
        };
        page._stringResolver = new UiStringResolver(cultures, "en-US");

        Assert.Equal("Hello", page.TestLocalize("Test.Title"));
    }

    [Fact]
    public void NotifyCultureChanged_fires_property_changed_for_all()
    {
        var page = new TestPage();
        string? changedProperty = "not-fired";
        page.PropertyChanged += (_, args) => changedProperty = args.PropertyName;

        page.NotifyCultureChanged();

        Assert.Equal(string.Empty, changedProperty);
    }

    // --- NullInstallerEngine Property Tests ---

    [Fact]
    public void SetProperty_StoresValue()
    {
        var engine = new NullInstallerEngine();

        engine.SetProperty("DBSERVER", "sql01");
        engine.SetProperty("DBSERVER", "sql02"); // overwrite succeeds without throwing
    }

    [Fact]
    public async Task SetSecureProperty_StoresAndDisposesOnShutdown()
    {
        var engine = new NullInstallerEngine();
        var data = System.Text.Encoding.UTF8.GetBytes("secret");
        var bytes = new SensitiveBytes(data);

        engine.SetSecureProperty("DBPASSWORD", bytes);
        await engine.ShutdownAsync();

        // After shutdown, the SensitiveBytes should be disposed (zeroed)
        Assert.All(data, b => Assert.Equal(0, b));
    }

    // --- Lifecycle Hook Defaults ---

    [Fact]
    public async Task OnDetectBeginAsync_DefaultBehavior_ReturnsTrue()
    {
        var page = new TestPage();

        var result = await page.OnDetectBeginAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task OnDetectCompleteAsync_DefaultBehavior_ReturnsCompletedTask()
    {
        var page = new TestPage();

        var task = page.OnDetectCompleteAsync(new DetectResult(InstallState.NotInstalled, null, []));

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task OnPlanBeginAsync_DefaultBehavior_ReturnsTrue()
    {
        var page = new TestPage();

        var result = await page.OnPlanBeginAsync(InstallAction.Install);

        Assert.True(result);
    }

    [Fact]
    public async Task OnPlanCompleteAsync_DefaultBehavior_ReturnsCompletedTask()
    {
        var page = new TestPage();

        var task = page.OnPlanCompleteAsync(new PlanResult([], 0L));

        Assert.True(task.IsCompleted);
        await task;
    }

    [Fact]
    public async Task OnApplyBeginAsync_DefaultBehavior_ReturnsTrue()
    {
        var page = new TestPage();

        var result = await page.OnApplyBeginAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task OnApplyCompleteAsync_DefaultBehavior_ReturnsCompletedTask()
    {
        var page = new TestPage();

        var task = page.OnApplyCompleteAsync(new ApplyResult(0, null));

        Assert.True(task.IsCompleted);
        await task;
    }
}
