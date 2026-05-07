namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;

/// <summary>
/// Drives an <see cref="IInstallerPipeline"/> by reading <see cref="UiRequest"/> events
/// from an <see cref="IUiChannel"/> and invoking the appropriate pipeline phase methods.
/// This is the production "event loop" that replaces <c>EngineStateMachine</c>.
///
/// <para>Phase protocol:</para>
/// <list type="number">
///   <item><description>Wait for <see cref="UiRequest.Detect"/> → call
///   <see cref="IInstallerPipeline.DetectAsync"/>.</description></item>
///   <item><description>Wait for <see cref="UiRequest.Plan"/> → call
///   <see cref="IInstallerPipeline.PlanAsync"/>.</description></item>
///   <item><description>Wait for <see cref="UiRequest.Apply"/> → call
///   <see cref="IInstallerPipeline.ApplyAsync"/>.</description></item>
///   <item><description><see cref="UiRequest.Cancel"/> or <see cref="UiRequest.Shutdown"/>
///   at any point → emit <see cref="PipelineEvent.PhaseChanged"/> for
///   <see cref="EnginePhase.Shutdown"/> and return exit code 0.</description></item>
/// </list>
/// </summary>
public sealed class PipelineRunner
{
    private readonly IInstallerPipeline _pipeline;
    private readonly IUiChannel _uiChannel;
    private readonly IEngineLogger? _logger;
    private readonly bool _isPlanOnly;
    private readonly string? _planOnlyOutputPath;

    public PipelineRunner(
        IInstallerPipeline pipeline,
        IUiChannel uiChannel,
        IEngineLogger? logger = null,
        bool isPlanOnly = false,
        string? planOnlyOutputPath = null)
    {
        _pipeline = pipeline;
        _uiChannel = uiChannel;
        _logger = logger;
        _isPlanOnly = isPlanOnly;
        _planOnlyOutputPath = planOnlyOutputPath;
    }

    /// <summary>
    /// Runs the installer lifecycle. Returns 0 on success, 1 on failure.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        _logger?.Info("PipelineRunner", "Session started");

        try
        {
            await foreach (var request in _uiChannel.ReadRequestsAsync(ct))
            {
                switch (request)
                {
                    case UiRequest.Detect:
                        _logger?.Info("PipelineRunner", "Detect requested");
                        var detectResult = await _pipeline.DetectAsync(ct);
                        if (detectResult.IsFailure)
                        {
                            _logger?.Error("PipelineRunner", $"Detect failed: {detectResult.Error.Message}");
                            await _uiChannel.SendAsync(
                                new PipelineEvent.Failed(detectResult.Error.Kind, detectResult.Error.Message), ct);
                            await SendShutdownAsync(ct);
                            return 1;
                        }
                        break;

                    case UiRequest.Plan planReq:
                        _logger?.Info("PipelineRunner", $"Plan requested: action={planReq.Action}");
                        var planResult = await _pipeline.PlanAsync(planReq, ct);
                        if (planResult.IsFailure)
                        {
                            _logger?.Error("PipelineRunner", $"Plan failed: {planResult.Error.Message}");
                            await _uiChannel.SendAsync(
                                new PipelineEvent.Failed(planResult.Error.Kind, planResult.Error.Message), ct);
                            await SendShutdownAsync(ct);
                            return 1;
                        }

                        // Plan-only mode: export the plan and exit without applying.
                        if (_isPlanOnly)
                        {
                            _logger?.Info("PipelineRunner", "Plan-only mode: exporting plan and shutting down");
                            var exportResult = _pipeline.ExportPlan(_planOnlyOutputPath);
                            if (exportResult.IsFailure)
                            {
                                _logger?.Error("PipelineRunner", $"Plan export failed: {exportResult.Error.Message}");
                                await _uiChannel.SendAsync(
                                    new PipelineEvent.Failed(exportResult.Error.Kind, exportResult.Error.Message), ct);
                                await SendShutdownAsync(ct);
                                return 1;
                            }

                            await _uiChannel.SendAsync(
                                new PipelineEvent.PhaseChanged(EnginePhase.Completing), ct);
                            await SendShutdownAsync(ct);
                            _logger?.Info("PipelineRunner", "Plan-only session completed");
                            return 0;
                        }

                        break;

                    case UiRequest.Apply:
                        _logger?.Info("PipelineRunner", "Apply requested");
                        var applyResult = await _pipeline.ApplyAsync(ct);
                        if (applyResult.IsFailure)
                        {
                            _logger?.Error("PipelineRunner", $"Apply failed: {applyResult.Error.Message}");
                            await _uiChannel.SendAsync(
                                new PipelineEvent.Failed(applyResult.Error.Kind, applyResult.Error.Message), ct);
                            await SendShutdownAsync(ct);
                            return 1;
                        }

                        // Apply succeeded: send Completing + Shutdown phase events
                        await _uiChannel.SendAsync(
                            new PipelineEvent.PhaseChanged(EnginePhase.Completing), ct);
                        await SendShutdownAsync(ct);
                        _logger?.Info("PipelineRunner", "Installation completed successfully");
                        return 0;

                    case UiRequest.LaunchUpdate:
                        // Update launch not yet wired in the pipeline — log and ignore
                        _logger?.Warning("PipelineRunner", "LaunchUpdate request received but not yet supported by pipeline runner.");
                        break;

                    case UiRequest.Cancel:
                    case UiRequest.Shutdown:
                        _logger?.Info("PipelineRunner", $"Shutdown requested ({request.GetType().Name})");
                        await SendShutdownAsync(ct);
                        return 0;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger?.Info("PipelineRunner", "Session cancelled by token");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.Error("PipelineRunner", $"Unhandled exception: {ex.Message}");
            try
            {
                await _uiChannel.SendAsync(
                    new PipelineEvent.Failed(ErrorKind.EngineError, ex.Message),
                    CancellationToken.None);
                await SendShutdownAsync(CancellationToken.None);
            }
            catch
            {
                // Best-effort — pipe may already be dead
            }
            return 1;
        }

        // Channel closed without explicit Shutdown (headless / EOF)
        await SendShutdownAsync(CancellationToken.None);
        _logger?.Info("PipelineRunner", "Session ended (channel closed)");
        return 0;
    }

    private async Task SendShutdownAsync(CancellationToken ct)
    {
        try
        {
            await _uiChannel.SendAsync(new PipelineEvent.PhaseChanged(EnginePhase.Shutdown), ct);
        }
        catch
        {
            // Best-effort: pipe may already be closing
        }
    }
}
