namespace FalkForge.Engine.Pipeline;

using FalkForge.Diagnostics;
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
    private readonly IFalkLogger? _logger;
    private readonly bool _isPlanOnly;
    private readonly string? _planOnlyOutputPath;

    public PipelineRunner(
        IInstallerPipeline pipeline,
        IUiChannel uiChannel,
        IFalkLogger? logger = null,
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
    /// Runs the installer lifecycle.
    /// Returns 0 on success or clean cancel/shutdown,
    /// 1 on error, 3 when a token-cancellation mid-apply triggered rollback.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        _logger?.Info("PipelineRunner", "Session started");

        // Tracks whether Apply was actually dispatched; used to decide whether
        // a token-cancellation OCE should trigger rollback. If the token is cancelled
        // before Apply starts, no packages have been touched and rollback is pointless.
        var applyDispatched = false;

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
                            EngineMeter.RecordError(detectResult.Error.Kind);
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
                            EngineMeter.RecordError(planResult.Error.Kind);
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
                                EngineMeter.RecordError(exportResult.Error.Kind);
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

                        // Elevation: run automatically after Plan (no-op when no gateway configured).
                        _logger?.Info("PipelineRunner", "Running elevation phase");
                        var elevateResult = await _pipeline.ElevateAsync(ct);
                        if (elevateResult.IsFailure)
                        {
                            _logger?.Error("PipelineRunner", $"Elevation failed: {elevateResult.Error.Message}");
                            EngineMeter.RecordError(elevateResult.Error.Kind);
                            await _uiChannel.SendAsync(
                                new PipelineEvent.Failed(elevateResult.Error.Kind, elevateResult.Error.Message), ct);
                            await SendShutdownAsync(ct);
                            return 1;
                        }

                        break;

                    case UiRequest.Apply:
                        _logger?.Info("PipelineRunner", "Apply requested");
                        applyDispatched = true;
                        var applyResult = await _pipeline.ApplyAsync(ct);
                        if (applyResult.IsFailure)
                        {
                            _logger?.Error("PipelineRunner", $"Apply failed: {applyResult.Error.Message}");
                            EngineMeter.RecordError(applyResult.Error.Kind);
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
                        _logger?.Info("PipelineRunner", "LaunchUpdate requested");
                        var launchResult = _pipeline.LaunchUpdate();
                        if (launchResult.IsFailure)
                        {
                            // Surface the typed failure to the UI (e.g. Authenticode refusal must
                            // show an error, never silence). The session continues so the user can
                            // proceed with the current installer or retry.
                            _logger?.Warning("PipelineRunner",
                                $"Update launch refused: {launchResult.Error.Message}");
                            EngineMeter.RecordError(launchResult.Error.Kind);
                            await _uiChannel.SendAsync(
                                new PipelineEvent.Failed(launchResult.Error.Kind, launchResult.Error.Message), ct);
                            break;
                        }

                        // Handoff: the update installer is now running. Shut the current engine
                        // down through the normal shutdown path so log/journal flush runs and the
                        // two installers do not fight over the same bundle.
                        _logger?.Info("PipelineRunner", "Update launched — shutting down for handoff");
                        await SendShutdownAsync(ct);
                        return 0;

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
            if (applyDispatched)
            {
                // Token was cancelled while Apply was in progress — partial installation may have
                // occurred. Run rollback to undo journaled changes, then signal RolledBack state
                // to the host via exit code 3 (mapped to EngineTerminalState.RolledBack).
                _logger?.Info("PipelineRunner", "Session cancelled mid-apply — triggering rollback");
                EngineMeter.RecordError(ErrorKind.ExecutionError);
                try
                {
                    await _pipeline.RollbackAsync(CancellationToken.None);
                }
                catch
                {
                    // Best-effort: rollback failures are logged by RollbackStep itself
                }

                try
                {
                    await _uiChannel.SendAsync(
                        new PipelineEvent.PhaseChanged(EnginePhase.Shutdown), CancellationToken.None);
                }
                catch
                {
                    // Best-effort: pipe may already be closing
                }

                return 3; // RolledBack
            }

            // Token cancelled before any apply work — clean shutdown, no rollback needed.
            _logger?.Info("PipelineRunner", "Session cancelled by token (before apply)");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger?.Error("PipelineRunner", $"Unhandled exception: {ex.Message}");
            EngineMeter.RecordError(ErrorKind.EngineError);
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
