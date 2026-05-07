namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Cross-process UI communication port. Hides binary message framing, pipe security
/// handshake, and the twenty-five <c>EngineMessage</c> subtypes from pipeline code.
/// </summary>
public interface IUiChannel : IAsyncDisposable
{
    /// <summary>
    /// Sends an event to the connected UI process. Returns immediately when no UI
    /// is connected (headless/CLI mode).
    /// </summary>
    Task SendAsync(PipelineEvent evt, CancellationToken ct);

    /// <summary>
    /// Produces a stream of UI requests. Completes when the pipe closes, the
    /// cancellation token fires, or a <see cref="UiRequest.Shutdown"/> is received.
    /// </summary>
    IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct);
}
