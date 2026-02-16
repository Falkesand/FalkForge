namespace FalkForge.Ui.Abstractions;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;

public interface IInstallerEngine
{
    Task<DetectResult> DetectAsync(CancellationToken ct = default);
    Task<PlanResult> PlanAsync(InstallAction action, CancellationToken ct = default);
    Task<ApplyResult> ApplyAsync(CancellationToken ct = default);
    IObservable<EnginePhase> Phase { get; }
    IObservable<InstallProgress> Progress { get; }
    IObservable<string> StatusMessage { get; }
    InstallerManifest Manifest { get; }
    InstallState DetectedState { get; }
    IReadOnlyList<FeatureState> Features { get; }
    string InstallDirectory { get; set; }
    void Cancel();
    Task<int> ShutdownAsync();
}
