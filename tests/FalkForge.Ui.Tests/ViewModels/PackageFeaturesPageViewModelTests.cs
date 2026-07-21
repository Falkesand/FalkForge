namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Stage 5 of the per-package MSI feature picker: the VM builds one section per advertised package,
/// nests features by parent, excludes hidden (Display 0) and absent (Level 0) features, and pushes
/// the checked-feature-id set to the engine on every toggle. The shell registers the page only when
/// features actually arrived (never an empty picker).
/// </summary>
public sealed class PackageFeaturesPageViewModelTests
{
    private static MsiFeatureInfo Feature(
        string id, string? parent = null, int level = 1, int display = 1, string? title = null) =>
        new(id, title, Description: null, parent, level, display, EstimatedSize: 0);

    [Fact]
    public void Sections_OnePerAdvertisedPackage()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures =
            {
                ["PkgA"] = [Feature("A1")],
                ["PkgB"] = [Feature("B1")]
            }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        Assert.Equal(2, vm.Sections.Count);
        Assert.Contains(vm.Sections, s => s.PackageId == "PkgA");
        Assert.Contains(vm.Sections, s => s.PackageId == "PkgB");
    }

    [Fact]
    public void Sections_Empty_WhenEngineNotAFeatureChannel()
    {
        var vm = new PackageFeaturesPageViewModel(new NonChannelEngine(), new StubNavigation());

        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void Tree_NestsChildUnderParent()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures =
            {
                ["Pkg"] = [Feature("Root", display: 1), Feature("Child", parent: "Root", display: 2)]
            }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        var section = Assert.Single(vm.Sections);
        var root = Assert.Single(section.Roots);
        Assert.Equal("Root", root.FeatureId);
        var child = Assert.Single(root.Children);
        Assert.Equal("Child", child.FeatureId);
    }

    [Fact]
    public void Tree_ExcludesLevelZeroAndDisplayZero()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures =
            {
                ["Pkg"] =
                [
                    Feature("Visible", level: 1, display: 1),
                    Feature("Absent", level: 0, display: 1),   // Level 0 → excluded
                    Feature("Hidden", level: 1, display: 0)     // Display 0 → excluded
                ]
            }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        var section = Assert.Single(vm.Sections);
        var root = Assert.Single(section.Roots);
        Assert.Equal("Visible", root.FeatureId);
        Assert.Equal(["Visible"], section.SelectedFeatureIds);
    }

    [Fact]
    public void Tree_ReparentsVisibleChildOfHiddenParentToRoot()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures =
            {
                ["Pkg"] =
                [
                    Feature("HiddenParent", display: 0),
                    Feature("VisibleChild", parent: "HiddenParent", display: 1)
                ]
            }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        var section = Assert.Single(vm.Sections);
        var root = Assert.Single(section.Roots);
        Assert.Equal("VisibleChild", root.FeatureId);
    }

    [Fact]
    public void AllVisibleFeatures_SelectedByDefault()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures = { ["Pkg"] = [Feature("A"), Feature("B", parent: "A")] }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        var section = Assert.Single(vm.Sections);
        Assert.Equal(["A", "B"], section.SelectedFeatureIds);
    }

    [Fact]
    public void TogglingNodeOff_SendsSelectionWithoutThatFeature()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures = { ["Pkg"] = [Feature("A", display: 1), Feature("B", display: 2)] }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());
        var section = Assert.Single(vm.Sections);

        // Uncheck "A" → engine is told the remaining selection is just "B".
        section.Roots[0].IsChecked = false;

        var sent = Assert.Single(engine.SentSelections);
        Assert.Equal("Pkg", sent.PackageId);
        Assert.Equal(["B"], sent.FeatureIds);
    }

    [Fact]
    public void TogglingNodeBackOn_SendsSelectionIncludingThatFeature()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures = { ["Pkg"] = [Feature("A", display: 1), Feature("B", display: 2)] }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());
        var section = Assert.Single(vm.Sections);

        section.Roots[0].IsChecked = false;
        section.Roots[0].IsChecked = true;

        var sent = engine.SentSelections[^1];
        Assert.Equal(["A", "B"], sent.FeatureIds);
    }

    // ── Shell registration ──────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WhenFeaturesAdvertised_RegistersPickerAfterFeaturesPage()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures = { ["Pkg"] = [Feature("A")] }
        };
        var shell = new DefaultShellViewModel(engine);

        await shell.InitializeAsync();

        Assert.Equal(8, shell.Pages.Count);
        var featuresIndex = IndexOf<FeaturesPageViewModel>(shell.Pages);
        Assert.IsType<PackageFeaturesPageViewModel>(shell.Pages[featuresIndex + 1]);
    }

    [Fact]
    public async Task InitializeAsync_WhenNoFeaturesAdvertised_DoesNotRegisterPicker()
    {
        var engine = new FeatureChannelEngine(); // empty PackageMsiFeatures
        var shell = new DefaultShellViewModel(engine);

        await shell.InitializeAsync();

        Assert.Equal(7, shell.Pages.Count);
        Assert.DoesNotContain(shell.Pages, p => p is PackageFeaturesPageViewModel);
    }

    [WpfFact]
    public void Page_BuildsAndBindsToViewModel()
    {
        var engine = new FeatureChannelEngine
        {
            PackageMsiFeatures = { ["Pkg"] = [Feature("Root"), Feature("Child", parent: "Root")] }
        };
        var vm = new PackageFeaturesPageViewModel(engine, new StubNavigation());

        var page = new FalkForge.Ui.Views.PackageFeaturesPage { DataContext = vm };

        Assert.Same(vm, page.DataContext);
    }

    private static int IndexOf<T>(IReadOnlyList<InstallerPageViewModel> pages)
    {
        for (var i = 0; i < pages.Count; i++)
            if (pages[i] is T)
                return i;
        return -1;
    }

    private sealed class StubNavigation : INavigationService
    {
        public InstallerPageViewModel? CurrentPage => null;
        public bool CanGoBack => false;
        public bool CanGoNext => false;
        public IReadOnlyList<InstallerPageViewModel> Pages => [];
        public Task NavigateNext() => Task.CompletedTask;
        public Task NavigateBack() => Task.CompletedTask;
        public Task NavigateTo(InstallerPageViewModel page) => Task.CompletedTask;
        public Task NavigateTo<T>() where T : InstallerPageViewModel => Task.CompletedTask;
    }

    private sealed class NonChannelEngine : StubEngineBase
    {
    }

    private sealed class FeatureChannelEngine : StubEngineBase, IPackageMsiFeatureChannel
    {
        private readonly Dictionary<string, IReadOnlyList<MsiFeatureInfo>> _features = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<MsiFeatureInfo>> PackageMsiFeatures => _features;

        IReadOnlyDictionary<string, IReadOnlyList<MsiFeatureInfo>> IPackageMsiFeatureChannel.PackageMsiFeatures => _features;

        public List<(string PackageId, IReadOnlyList<string> FeatureIds)> SentSelections { get; } = [];

        public void SetPackageFeatureSelection(string packageId, IReadOnlyList<string> selectedFeatureIds)
        {
            SentSelections.Add((packageId, [.. selectedFeatureIds]));
        }
    }

    private abstract class StubEngineBase : IInstallerEngine
    {
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
        public string? InstalledProductVersion => null;
        public IReadOnlyList<FeatureState> Features => [];
        public string InstallDirectory { get; set; } = @"C:\Program Files\TestProduct";
        public string? LogPath => null;
        public IObservable<EnginePhase> Phase => new EmptyObservable<EnginePhase>();
        public IObservable<InstallProgress> Progress => new EmptyObservable<InstallProgress>();
        public IObservable<string> StatusMessage => new EmptyObservable<string>();

        public Task<DetectResult> DetectAsync(CancellationToken ct = default)
            => Task.FromResult(new DetectResult(InstallState.NotInstalled, null, []));

        public Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
            => Task.FromResult(new PlanResult([], 0L));

        public Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
            => Task.FromResult(new ApplyResult(0, null));

        public void Cancel() { }
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

            private sealed class EmptyDisposable : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
