namespace FalkForge.Ui.Abstractions.Tests;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;

internal sealed class TestInstallerEngine : IInstallerEngine
{
    public InstallerManifest Manifest { get; set; } = new()
    {
        Name = "TestProduct",
        Manufacturer = "TestCorp",
        Version = "1.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Packages = [],
        Scope = InstallScope.PerUser
    };

    public InstallState DetectedState { get; set; } = InstallState.NotInstalled;

    public IReadOnlyList<FeatureState> Features { get; set; } = [];

    public string InstallDirectory { get; set; } = @"C:\Program Files\TestProduct";

    public IObservable<EnginePhase> Phase => new EmptyObservable<EnginePhase>();
    public IObservable<InstallProgress> Progress => new EmptyObservable<InstallProgress>();
    public IObservable<string> StatusMessage => new EmptyObservable<string>();

    public Task<DetectResult> DetectAsync(CancellationToken ct = default)
        => Task.FromResult(new DetectResult(DetectedState, "1.0.0", []));

    public Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
        => Task.FromResult(new PlanResult(["Install TestPackage"], 1024L));

    public Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
        => Task.FromResult(new ApplyResult(0, null));

    public bool CancelCalled { get; private set; }

    public void Cancel() => CancelCalled = true;

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
