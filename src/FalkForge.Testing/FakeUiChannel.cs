namespace FalkForge.Testing;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FalkForge.Engine.Pipeline;

/// <summary>
/// In-memory <see cref="IUiChannel"/> for tests. Records all outbound
/// <see cref="PipelineEvent"/> values and allows tests to inject inbound
/// <see cref="UiRequest"/> values via <see cref="EnqueueRequest"/>.
/// </summary>
public sealed class FakeUiChannel : IUiChannel
{
    private readonly List<PipelineEvent> _sentEvents = [];
    private readonly Channel<UiRequest> _requests =
        Channel.CreateUnbounded<UiRequest>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false
        });
    private readonly Lock _lock = new();

    /// <summary>All events sent via <see cref="SendAsync"/> in order.</summary>
    public IReadOnlyList<PipelineEvent> SentEvents
    {
        get { lock (_lock) { return _sentEvents.ToArray(); } }
    }

    /// <summary>
    /// The last session correlation id received via
    /// <see cref="SetSessionCorrelationId"/>. <see cref="Guid.Empty"/> until set.
    /// Exposed so tests can assert that <c>EngineSession.BindToChannel</c>
    /// propagates the id to the channel.
    /// </summary>
    public Guid LastSessionCorrelationId { get; private set; }

    /// <inheritdoc/>
    public void SetSessionCorrelationId(Guid id) => LastSessionCorrelationId = id;

    /// <summary>
    /// Injects a <see cref="UiRequest"/> that will be yielded by
    /// <see cref="ReadRequestsAsync"/>. Call <see cref="Complete"/> when done
    /// to end the request stream.
    /// </summary>
    public void EnqueueRequest(UiRequest request) =>
        _requests.Writer.TryWrite(request);

    /// <summary>Signals end-of-stream for <see cref="ReadRequestsAsync"/>.</summary>
    public void Complete() => _requests.Writer.TryComplete();

    /// <inheritdoc/>
    public Task SendAsync(PipelineEvent evt, CancellationToken ct)
    {
        lock (_lock) { _sentEvents.Add(evt); }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var req in _requests.Reader.ReadAllAsync(ct))
        {
            yield return req;
            if (req is UiRequest.Shutdown) yield break;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        return default;
    }
}
