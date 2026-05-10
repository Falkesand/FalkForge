namespace FalkForge.Engine.Pipeline;

/// <summary>
/// Cross-process UI communication port. Hides binary message framing, pipe security
/// handshake, and the twenty-five <c>EngineMessage</c> subtypes from pipeline code.
/// </summary>
public interface IUiChannel : IAsyncDisposable
{
    /// <summary>
    /// Stamps the session correlation id on this channel so that outgoing
    /// <c>LogMessage</c> and <c>PhaseChangedMessage</c> frames carry the same id
    /// as the on-disk log file. Called once by <see cref="FalkForge.Engine.EngineSession"/>
    /// before any events are sent. Implementations that do not emit wire frames
    /// (e.g. test doubles) may store the value for assertion purposes.
    /// </summary>
    void SetSessionCorrelationId(Guid id);

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
