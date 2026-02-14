namespace FalkInstaller.Engine;

using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Protocol.Manifest;
using FalkInstaller.Engine.Protocol.Transport;
using FalkInstaller.Platform;

public sealed class EngineContext
{
    public required InstallerManifest Manifest { get; init; }
    public required IPlatformServices Platform { get; init; }
    public required PipeServer? UiPipe { get; init; }
    public required CancellationToken ShutdownToken { get; init; }

    public InstallState DetectedState { get; set; }
    public string? DetectedVersion { get; set; }
    public FeatureState[] DetectedFeatures { get; set; } = [];
    public InstallAction RequestedAction { get; set; }
    public InstallPlan? CurrentPlan { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public string? ErrorMessage { get; set; }
}
