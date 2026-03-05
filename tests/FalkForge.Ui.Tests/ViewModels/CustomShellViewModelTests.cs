namespace FalkForge.Ui.Tests.ViewModels;

using System.Windows.Controls;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using Xunit;

public class ShellTestView : UserControl { }

public class PageOne : InstallerPage<ShellTestView>
{
    public override string Title => "Page One";
}

public class PageTwo : InstallerPage<ShellTestView>
{
    public override string Title => "Page Two";
    public override PageResult OnNext() => PageResult.Install;
}

public class PageThree : InstallerPage<ShellTestView>
{
    public override string Title => "Page Three";
}

public class StayPage : InstallerPage<ShellTestView>
{
    public override string Title => "Stay Page";
    public override PageResult OnNext() => PageResult.Stay("Validation failed");
}

public class GoToPage : InstallerPage<ShellTestView>
{
    public override string Title => "GoTo Page";
    public override PageResult OnNext() => PageResult.GoTo<PageThree>();
}

public class FinishPage : InstallerPage<ShellTestView>
{
    public override string Title => "Finish";
    public override PageResult OnNext() => PageResult.Finish;
}

public class CancelPage : InstallerPage<ShellTestView>
{
    public override string Title => "Cancel";
    public override PageResult OnNext() => PageResult.Cancel;
}

public class NoBackPage : InstallerPage<ShellTestView>
{
    public override string Title => "No Back";
    public override bool CanGoBack => false;
}

public class NoNextPage : InstallerPage<ShellTestView>
{
    public override string Title => "No Next";
    public override bool CanGoNext => false;
}

public class LifecyclePage : InstallerPage<ShellTestView>
{
    public override string Title => "Lifecycle";
    public bool NavigatedToCalled { get; private set; }
    public bool NavigatingFromCalled { get; private set; }

    public override Task OnNavigatedToAsync()
    {
        NavigatedToCalled = true;
        return Task.CompletedTask;
    }

    public override Task OnNavigatingFromAsync()
    {
        NavigatingFromCalled = true;
        return Task.CompletedTask;
    }
}

public class LifecycleHookPage : InstallerPage<ShellTestView>
{
    public override string Title => "Hooks";
    public List<string> HookLog { get; } = [];
    public bool CancelDetect { get; set; }
    public bool CancelPlan { get; set; }
    public bool CancelApply { get; set; }
    public DetectResult? LastDetectResult { get; private set; }
    public PlanResult? LastPlanResult { get; private set; }
    public ApplyResult? LastApplyResult { get; private set; }

    public override PageResult OnNext() => PageResult.Install;

    protected internal override Task<bool> OnDetectBeginAsync()
    {
        HookLog.Add("DetectBegin");
        return Task.FromResult(!CancelDetect);
    }

    protected internal override Task OnDetectCompleteAsync(DetectResult result)
    {
        HookLog.Add("DetectComplete");
        LastDetectResult = result;
        return Task.CompletedTask;
    }

    protected internal override Task<bool> OnPlanBeginAsync(InstallAction action)
    {
        HookLog.Add($"PlanBegin:{action}");
        return Task.FromResult(!CancelPlan);
    }

    protected internal override Task OnPlanCompleteAsync(PlanResult result)
    {
        HookLog.Add("PlanComplete");
        LastPlanResult = result;
        return Task.CompletedTask;
    }

    protected internal override Task<bool> OnApplyBeginAsync()
    {
        HookLog.Add("ApplyBegin");
        return Task.FromResult(!CancelApply);
    }

    protected internal override Task OnApplyCompleteAsync(ApplyResult result)
    {
        HookLog.Add("ApplyComplete");
        LastApplyResult = result;
        return Task.CompletedTask;
    }
}

public class CustomShellViewModelTests
{
    private static CustomShellViewModel CreateViewModel(
        IReadOnlyList<InstallerPage> pages,
        IInstallerEngine? engine = null)
    {
        var eng = engine ?? new TestInstallerEngine();
        var state = new InstallerState();
        foreach (var page in pages)
        {
            page.Engine = eng;
            page.SharedState = state;
        }
        return new CustomShellViewModel(pages, eng, state);
    }

    // --- Navigation ---

    [WpfFact]
    public async Task NavigateToFirstPage_SetsCurrentPage()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageTwo() };
        var vm = CreateViewModel(pages);

        await vm.NavigateToFirstPageAsync();

        Assert.Same(pages[0], vm.CurrentPage);
    }

    [WpfFact]
    public async Task NavigateToFirstPage_SetsCurrentView()
    {
        var pages = new InstallerPage[] { new PageOne() };
        var vm = CreateViewModel(pages);

        await vm.NavigateToFirstPageAsync();

        Assert.NotNull(vm.CurrentView);
        Assert.IsType<ShellTestView>(vm.CurrentView);
    }

    [WpfFact]
    public async Task OnNextAsync_AdvancesToNextPage()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Same(pages[1], vm.CurrentPage);
    }

    [WpfFact]
    public async Task OnNextAsync_AtLastPage_StaysOnLastPage()
    {
        var pages = new InstallerPage[] { new PageOne() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Same(pages[0], vm.CurrentPage);
    }

    [WpfFact]
    public async Task OnBackAsync_GoesToPreviousPage()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();
        await vm.OnNextAsync();

        await vm.OnBackAsync();

        Assert.Same(pages[0], vm.CurrentPage);
    }

    [WpfFact]
    public async Task OnBackAsync_AtFirstPage_StaysOnFirstPage()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnBackAsync();

        Assert.Same(pages[0], vm.CurrentPage);
    }

    [WpfFact]
    public async Task GoTo_NavigatesToTargetPage()
    {
        var pageThree = new PageThree();
        var pages = new InstallerPage[] { new GoToPage(), new PageOne(), pageThree };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Same(pageThree, vm.CurrentPage);
    }

    // --- Stay ---

    [WpfFact]
    public async Task Stay_RemainsOnCurrentPage()
    {
        var stayPage = new StayPage();
        var pages = new InstallerPage[] { stayPage, new PageOne() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Same(stayPage, vm.CurrentPage);
    }

    [WpfFact]
    public async Task Stay_SetsStatusMessage()
    {
        var pages = new InstallerPage[] { new StayPage(), new PageOne() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal("Validation failed", vm.StatusMessage);
    }

    // --- Engine Actions ---

    [WpfFact]
    public async Task Install_CallsPlanAndApply()
    {
        var engine = new TestInstallerEngine();
        var pages = new InstallerPage[] { new PageTwo(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(InstallAction.Install, engine.LastPlannedAction);
    }

    [WpfFact]
    public async Task Install_AdvancesToNextPage()
    {
        var pages = new InstallerPage[] { new PageTwo(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.IsType<PageThree>(vm.CurrentPage);
    }

    [WpfFact]
    public async Task Uninstall_CallsPlanWithUninstall()
    {
        var engine = new TestInstallerEngine();
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.ProcessResultAsync(PageResult.Uninstall);

        Assert.Equal(InstallAction.Uninstall, engine.LastPlannedAction);
    }

    [WpfFact]
    public async Task Repair_CallsPlanWithRepair()
    {
        var engine = new TestInstallerEngine();
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.ProcessResultAsync(PageResult.Repair);

        Assert.Equal(InstallAction.Repair, engine.LastPlannedAction);
    }

    [WpfFact]
    public async Task IsApplying_TrueDuringEngineAction()
    {
        var applyTcs = new TaskCompletionSource<ApplyResult>();
        var engine = new DelayingTestEngine(applyTcs.Task);
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        var actionTask = vm.ProcessResultAsync(PageResult.Install);

        Assert.True(vm.IsApplying);

        applyTcs.SetResult(new ApplyResult(0, null));
        await actionTask;

        Assert.False(vm.IsApplying);
    }

    // --- Close ---

    [WpfFact]
    public async Task Finish_RaisesCloseRequested()
    {
        var pages = new InstallerPage[] { new FinishPage() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();
        var closeRaised = false;
        vm.CloseRequested += (_, _) => closeRaised = true;

        await vm.OnNextAsync();

        Assert.True(closeRaised);
    }

    [WpfFact]
    public async Task Cancel_RaisesCloseRequested()
    {
        var pages = new InstallerPage[] { new CancelPage() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();
        var closeRaised = false;
        vm.CloseRequested += (_, _) => closeRaised = true;

        await vm.OnNextAsync();

        Assert.True(closeRaised);
    }

    [WpfFact]
    public async Task Cancel_DuringApply_CallsEngineCancel()
    {
        var applyTcs = new TaskCompletionSource<ApplyResult>();
        var engine = new DelayingTestEngine(applyTcs.Task);
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        var actionTask = vm.ProcessResultAsync(PageResult.Install);
        Assert.True(vm.IsApplying);

        await vm.OnCancelAsync();

        Assert.True(engine.CancelCalled);

        applyTcs.SetResult(new ApplyResult(0, null));
        await actionTask;
    }

    // --- Navigation Guards ---

    [WpfFact]
    public async Task CanGoBack_FirstPage_IsFalse()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        Assert.False(vm.CanGoBack);
    }

    [WpfFact]
    public async Task CanGoNext_RespectsPageCanGoNext()
    {
        var pages = new InstallerPage[] { new NoNextPage() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        Assert.False(vm.CanGoNext);
    }

    [WpfFact]
    public async Task CanGoBack_RespectsPageCanGoBack()
    {
        var pages = new InstallerPage[] { new PageOne(), new NoBackPage() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();
        await vm.OnNextAsync();

        Assert.False(vm.CanGoBack);
    }

    [WpfFact]
    public async Task CanGoBack_DuringApply_IsFalse()
    {
        var applyTcs = new TaskCompletionSource<ApplyResult>();
        var engine = new DelayingTestEngine(applyTcs.Task);
        var pages = new InstallerPage[] { new PageOne(), new PageThree(), new PageOne() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();
        await vm.OnNextAsync();

        var actionTask = vm.ProcessResultAsync(PageResult.Install);

        Assert.False(vm.CanGoBack);

        applyTcs.SetResult(new ApplyResult(0, null));
        await actionTask;
    }

    [WpfFact]
    public async Task CanGoNext_DuringApply_IsFalse()
    {
        var applyTcs = new TaskCompletionSource<ApplyResult>();
        var engine = new DelayingTestEngine(applyTcs.Task);
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        var actionTask = vm.ProcessResultAsync(PageResult.Install);

        Assert.False(vm.CanGoNext);

        applyTcs.SetResult(new ApplyResult(0, null));
        await actionTask;
    }

    // --- Lifecycle ---

    [WpfFact]
    public async Task Navigation_CallsOnNavigatingFromAsync()
    {
        var lifecyclePage = new LifecyclePage();
        var pages = new InstallerPage[] { lifecyclePage, new PageOne() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.True(lifecyclePage.NavigatingFromCalled);
    }

    [WpfFact]
    public async Task Navigation_CallsOnNavigatedToAsync()
    {
        var lifecyclePage = new LifecyclePage();
        var pages = new InstallerPage[] { new PageOne(), lifecyclePage };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.True(lifecyclePage.NavigatedToCalled);
    }

    // --- Empty pages list ---

    [Fact]
    public async Task NavigateToFirstPage_EmptyList_DoesNotThrow()
    {
        var vm = CreateViewModel([]);

        await vm.NavigateToFirstPageAsync();

        Assert.Null(vm.CurrentPage);
    }

    // --- PropertyChanged ---

    [WpfFact]
    public async Task Navigation_RaisesPropertyChanged_ForCurrentPage()
    {
        var pages = new InstallerPage[] { new PageOne(), new PageThree() };
        var vm = CreateViewModel(pages);
        await vm.NavigateToFirstPageAsync();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await vm.OnNextAsync();

        Assert.Contains(nameof(vm.CurrentPage), changed);
    }

    [WpfFact]
    public async Task OnCancelAsync_NotApplying_DoesNotCallEngineCancel()
    {
        var engine = new TestInstallerEngine();
        var pages = new InstallerPage[] { new PageOne() };
        var vm = CreateViewModel(pages, engine);
        await vm.NavigateToFirstPageAsync();

        await vm.OnCancelAsync();

        Assert.False(engine.CancelCalled);
    }

    // --- Lifecycle Hooks ---

    [WpfFact]
    public async Task ExecuteEngineAction_CallsAllLifecycleHooks_InOrder()
    {
        var hookPage = new LifecycleHookPage();
        var completePage = new PageThree();
        var vm = CreateViewModel([hookPage, completePage]);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(
            ["DetectBegin", "DetectComplete", "PlanBegin:Install", "PlanComplete", "ApplyBegin", "ApplyComplete"],
            hookPage.HookLog);
    }

    [WpfFact]
    public async Task ExecuteEngineAction_DetectBeginReturnsFalse_StopsExecution()
    {
        var hookPage = new LifecycleHookPage { CancelDetect = true };
        var completePage = new PageThree();
        var vm = CreateViewModel([hookPage, completePage]);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(["DetectBegin"], hookPage.HookLog);
    }

    [WpfFact]
    public async Task ExecuteEngineAction_PlanBeginReturnsFalse_StopsAfterDetect()
    {
        var hookPage = new LifecycleHookPage { CancelPlan = true };
        var completePage = new PageThree();
        var vm = CreateViewModel([hookPage, completePage]);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(["DetectBegin", "DetectComplete", "PlanBegin:Install"], hookPage.HookLog);
    }

    [WpfFact]
    public async Task ExecuteEngineAction_ApplyBeginReturnsFalse_StopsAfterPlan()
    {
        var hookPage = new LifecycleHookPage { CancelApply = true };
        var completePage = new PageThree();
        var vm = CreateViewModel([hookPage, completePage]);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(
            ["DetectBegin", "DetectComplete", "PlanBegin:Install", "PlanComplete", "ApplyBegin"],
            hookPage.HookLog);
    }

    [WpfFact]
    public async Task ExecuteEngineAction_HooksReceiveResults()
    {
        var hookPage = new LifecycleHookPage();
        var completePage = new PageThree();
        var vm = CreateViewModel([hookPage, completePage]);
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.NotNull(hookPage.LastDetectResult);
        Assert.NotNull(hookPage.LastPlanResult);
        Assert.NotNull(hookPage.LastApplyResult);
    }

    /// <summary>
    /// Test engine that delays ApplyAsync to allow observing IsApplying state.
    /// </summary>
    private sealed class DelayingTestEngine : IInstallerEngine
    {
        private readonly Task<ApplyResult> _applyTask;

        public DelayingTestEngine(Task<ApplyResult> applyTask)
        {
            _applyTask = applyTask;
        }

        public InstallerManifest Manifest { get; } = new()
        {
            Name = "TestProduct",
            Manufacturer = "TestCorp",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerUser
        };

        public InstallState DetectedState => InstallState.NotInstalled;
        public IReadOnlyList<FeatureState> Features => [];
        public string InstallDirectory { get; set; } = @"C:\Program Files\TestProduct";
        public IObservable<EnginePhase> Phase => new EmptyObservable<EnginePhase>();
        public IObservable<InstallProgress> Progress => new EmptyObservable<InstallProgress>();
        public IObservable<string> StatusMessage => new EmptyObservable<string>();

        public InstallAction? LastPlannedAction { get; private set; }
        public bool CancelCalled { get; private set; }

        public Task<DetectResult> DetectAsync(CancellationToken ct = default)
            => Task.FromResult(new DetectResult(DetectedState, "1.0.0", []));

        public Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
        {
            LastPlannedAction = action;
            return Task.FromResult(new PlanResult(["Install TestPackage"], 1024L));
        }

        public Task<ApplyResult> ApplyAsync(CancellationToken ct = default) => _applyTask;

        public void Cancel() => CancelCalled = true;

        public void LaunchUpdate() { }

        public void SetProperty(string name, string value) { }

        public void SetSecureProperty(string name, SensitiveBytes value) { }

        public Task<int> ShutdownAsync() => Task.FromResult(0);

        private sealed class EmptyObservable<T> : IObservable<T>
        {
            public IDisposable Subscribe(IObserver<T> observer)
            {
                observer.OnCompleted();
                return new EmptyDisposable();
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
