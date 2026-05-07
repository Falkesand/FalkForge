namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;

/// <summary>
/// Detect phase step. Loads the manifest (from embedded bytes or a layout store),
/// runs <see cref="PackageDetector"/> + dependency detection, emits
/// <see cref="PipelineEvent.PhaseChanged"/> for <see cref="EnginePhase.Detecting"/>,
/// and populates <see cref="PipelineContext.Detection"/>.
/// </summary>
internal sealed class DetectStep : IDetectStep
{
    private readonly InstallerManifest _manifest;
    private readonly IRegistry _registry;
    private readonly IUiChannel _uiChannel;

    public DetectStep(
        InstallerManifest manifest,
        IRegistry registry,
        IUiChannel uiChannel)
    {
        _manifest = manifest;
        _registry = registry;
        _uiChannel = uiChannel;
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        await _uiChannel.SendAsync(
            new PipelineEvent.PhaseChanged(EnginePhase.Detecting), ct);

        ctx.Manifest = _manifest;

        var detector = new PackageDetector(_registry);
        var detection = detector.Detect(_manifest);
        ctx.Detection = detection;

        await _uiChannel.SendAsync(
            new PipelineEvent.Log(LogLevel.Info,
                $"Detection complete: state={detection.State}, version={detection.CurrentVersion ?? "none"}"),
            ct);

        return Unit.Value;
    }
}
