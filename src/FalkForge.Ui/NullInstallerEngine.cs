namespace FalkForge.Ui;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;

internal sealed class NullInstallerEngine : IInstallerEngine
{
    private readonly Dictionary<string, string> _properties = [];
    private readonly Dictionary<string, SensitiveBytes> _secureProperties = [];

    public InstallerManifest Manifest { get; } = new()
    {
        Name = "Standalone",
        Manufacturer = string.Empty,
        Version = "0.0.0",
        BundleId = Guid.Empty,
        UpgradeCode = Guid.Empty,
        Packages = [],
        Scope = InstallScope.PerUser
    };

    public InstallState DetectedState { get; } = InstallState.NotInstalled;
    public IReadOnlyList<FeatureState> Features { get; } = [];
    public string InstallDirectory { get; set; } = string.Empty;

    public IObservable<EnginePhase> Phase => EmptyObservable<EnginePhase>.Instance;
    public IObservable<InstallProgress> Progress => EmptyObservable<InstallProgress>.Instance;
    public IObservable<string> StatusMessage => EmptyObservable<string>.Instance;

    public Task<DetectResult> DetectAsync(CancellationToken ct = default)
        => Task.FromResult(new DetectResult(InstallState.NotInstalled, null, []));

    public Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default)
        => Task.FromResult(new PlanResult([], 0L));

    public Task<ApplyResult> ApplyAsync(CancellationToken ct = default)
        => Task.FromResult(new ApplyResult(0, null));

    public void Cancel() { }

    public void SetProperty(string name, string value)
        => _properties[name] = value;

    public void SetSecureProperty(string name, SensitiveBytes value)
    {
        if (_secureProperties.TryGetValue(name, out var existing))
            existing.Dispose();
        _secureProperties[name] = value;
    }

    public Task<int> ShutdownAsync()
    {
        foreach (var sp in _secureProperties.Values)
            sp.Dispose();
        _secureProperties.Clear();
        return Task.FromResult(0);
    }

    private sealed class EmptyObservable<T> : IObservable<T>
    {
        public static readonly EmptyObservable<T> Instance = new();

        public IDisposable Subscribe(IObserver<T> observer)
        {
            observer.OnCompleted();
            return EmptyDisposable.Instance;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
