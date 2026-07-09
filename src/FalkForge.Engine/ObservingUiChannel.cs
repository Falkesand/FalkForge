using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol;

namespace FalkForge.Engine;

/// <summary>
/// Thin <see cref="IUiChannel"/> decorator that records whether a
/// <see cref="PipelineEvent.PhaseChanged"/> for <see cref="EnginePhase.Completing"/>
/// was emitted. Used by <see cref="EngineSession.RunUntilShutdown"/> to distinguish a
/// successful Apply (Completing emitted) from a user Cancel (only Shutdown emitted).
/// </summary>
internal sealed class ObservingUiChannel : IUiChannel
{
    private readonly IUiChannel _inner;

    public bool CompletingEmitted { get; private set; }

    public ObservingUiChannel(IUiChannel inner) => _inner = inner;

    public void SetSessionCorrelationId(Guid id) => _inner.SetSessionCorrelationId(id);

    public Task SendAsync(PipelineEvent evt, CancellationToken ct)
    {
        if (evt is PipelineEvent.PhaseChanged { Phase: EnginePhase.Completing })
            CompletingEmitted = true;
        return _inner.SendAsync(evt, ct);
    }

    public IAsyncEnumerable<UiRequest> ReadRequestsAsync(CancellationToken ct) =>
        _inner.ReadRequestsAsync(ct);

    public ValueTask DisposeAsync() => default; // lifetime owned by EngineSession
}
