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

    /// <summary>
    /// Sets a property value to pass to MSI packages during installation.
    /// </summary>
    void SetProperty(string name, string value);

    /// <summary>
    /// Sets a secure property value transported via named pipe to the MSI session.
    /// Never appears on the command line or in logs.
    /// </summary>
    /// <remarks>
    /// Implementations must consume the bytes synchronously before returning.
    /// The caller will dispose the <paramref name="value"/> after this method returns.
    /// </remarks>
    void SetSecureProperty(string name, SensitiveBytes value);
}
