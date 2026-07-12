namespace FalkForge.Ui.Tests.ViewModels;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.ViewModels;
using Xunit;

/// <summary>
/// Verifies that the shell forwards the engine's per-package / per-related-bundle lifecycle
/// events to the active page's granular hooks, interleaved with the phase-level hooks in the
/// exact documented order. The hooks are observational (no veto).
/// </summary>
public sealed class CustomShellViewModelPerPackageTests
{
    private sealed class GranularHookPage : InstallerPage<ShellTestView>
    {
        public override string Title => "Granular";
        public List<string> HookLog { get; } = [];

        public override PageResult OnNext() => PageResult.Install;

        protected internal override Task<bool> OnDetectBeginAsync()
        {
            HookLog.Add("DetectBegin");
            return Task.FromResult(true);
        }

        protected internal override Task OnDetectPackageCompleteAsync(PackageDetectInfo info)
        {
            HookLog.Add($"DetectPkg:{info.PackageId}:{info.State}");
            return Task.CompletedTask;
        }

        protected internal override Task OnDetectRelatedBundleAsync(RelatedBundleInfo info)
        {
            HookLog.Add($"DetectRelated:{info.BundleId}");
            return Task.CompletedTask;
        }

        protected internal override Task OnDetectCompleteAsync(DetectResult result)
        {
            HookLog.Add("DetectComplete");
            return Task.CompletedTask;
        }

        protected internal override Task<bool> OnPlanBeginAsync(InstallAction action)
        {
            HookLog.Add($"PlanBegin:{action}");
            return Task.FromResult(true);
        }

        protected internal override Task OnPlanPackageBeginAsync(PackagePlanInfo info)
        {
            HookLog.Add($"PlanPkgBegin:{info.PackageId}:{info.PlannedAction}");
            return Task.CompletedTask;
        }

        protected internal override Task OnPlanPackageCompleteAsync(PackagePlanInfo info)
        {
            HookLog.Add($"PlanPkgComplete:{info.PackageId}");
            return Task.CompletedTask;
        }

        protected internal override Task OnPlanCompleteAsync(PlanResult result)
        {
            HookLog.Add("PlanComplete");
            return Task.CompletedTask;
        }

        protected internal override Task<bool> OnApplyBeginAsync()
        {
            HookLog.Add("ApplyBegin");
            return Task.FromResult(true);
        }

        protected internal override Task OnApplyPackageBeginAsync(PackageApplyBeginInfo info)
        {
            HookLog.Add($"ApplyPkgBegin:{info.PackageId}");
            return Task.CompletedTask;
        }

        protected internal override Task OnApplyPackageCompleteAsync(PackageApplyCompleteInfo info)
        {
            HookLog.Add($"ApplyPkgComplete:{info.PackageId}:{info.Succeeded}");
            return Task.CompletedTask;
        }

        protected internal override Task OnApplyCompleteAsync(ApplyResult result)
        {
            HookLog.Add("ApplyComplete");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Engine double that raises the granular per-package events synchronously inside the
    /// Detect/Plan/Apply round-trips, exactly as the production engine interleaves them.
    /// </summary>
    private sealed class GranularEventEngine : IInstallerEngine, IPackageLifecycleEvents
    {
        private static readonly string[] Packages = ["Pkg1", "Pkg2"];

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

        public event Action<PackageDetectInfo>? PackageDetected;
        public event Action<RelatedBundleInfo>? RelatedBundleDetected;
        public event Action<PackagePlanInfo>? PackagePlanBeginning;
        public event Action<PackagePlanInfo>? PackagePlanCompleted;
        public event Action<PackageApplyBeginInfo>? PackageApplyBeginning;
        public event Action<PackageApplyCompleteInfo>? PackageApplyCompleted;

        public Task<DetectResult> DetectAsync(CancellationToken ct = default)
        {
            foreach (var p in Packages)
                PackageDetected?.Invoke(new PackageDetectInfo(p, InstallState.NotInstalled, null));
            RelatedBundleDetected?.Invoke(
                new RelatedBundleInfo("bundle-x", RelatedBundleRelation.Upgrade, "0.9.0"));
            return Task.FromResult(new DetectResult(InstallState.NotInstalled, null, []));
        }

        public Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
        {
            foreach (var p in Packages)
            {
                PackagePlanBeginning?.Invoke(new PackagePlanInfo(p, $"Display {p}", "Install"));
                PackagePlanCompleted?.Invoke(new PackagePlanInfo(p, $"Display {p}", "Install"));
            }

            return Task.FromResult(new PlanResult(Packages, 1024L));
        }

        public Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
        {
            foreach (var p in Packages)
            {
                PackageApplyBeginning?.Invoke(new PackageApplyBeginInfo(p, $"Display {p}"));
                PackageApplyCompleted?.Invoke(new PackageApplyCompleteInfo(p, $"Display {p}", true));
            }

            return Task.FromResult(new ApplyResult(0, null));
        }

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
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private static CustomShellViewModel CreateViewModel(IReadOnlyList<InstallerPage> pages, IInstallerEngine engine)
    {
        var state = new InstallerState();
        foreach (var page in pages)
        {
            page.Engine = engine;
            page.SharedState = state;
        }

        return new CustomShellViewModel(pages, engine, state);
    }

    [WpfFact]
    public async Task GranularHooks_FireInterleaved_InDocumentedOrder()
    {
        var hookPage = new GranularHookPage();
        var vm = CreateViewModel([hookPage, new PageThree()], new GranularEventEngine());
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(
            [
                "DetectBegin",
                "DetectPkg:Pkg1:NotInstalled",
                "DetectPkg:Pkg2:NotInstalled",
                "DetectRelated:bundle-x",
                "DetectComplete",
                "PlanBegin:Install",
                "PlanPkgBegin:Pkg1:Install",
                "PlanPkgComplete:Pkg1",
                "PlanPkgBegin:Pkg2:Install",
                "PlanPkgComplete:Pkg2",
                "PlanComplete",
                "ApplyBegin",
                "ApplyPkgBegin:Pkg1",
                "ApplyPkgComplete:Pkg1:True",
                "ApplyPkgBegin:Pkg2",
                "ApplyPkgComplete:Pkg2:True",
                "ApplyComplete",
            ],
            hookPage.HookLog);
    }

    [WpfFact]
    public async Task GranularHooks_FireOncePerPackage()
    {
        var hookPage = new GranularHookPage();
        var vm = CreateViewModel([hookPage, new PageThree()], new GranularEventEngine());
        await vm.NavigateToFirstPageAsync();

        await vm.OnNextAsync();

        Assert.Equal(2, hookPage.HookLog.Count(l => l.StartsWith("DetectPkg:", StringComparison.Ordinal)));
        Assert.Equal(2, hookPage.HookLog.Count(l => l.StartsWith("ApplyPkgBegin:", StringComparison.Ordinal)));
        Assert.Equal(2, hookPage.HookLog.Count(l => l.StartsWith("ApplyPkgComplete:", StringComparison.Ordinal)));
    }
}
