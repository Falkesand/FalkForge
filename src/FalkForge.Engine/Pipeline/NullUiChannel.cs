namespace FalkForge.Engine.Pipeline;

using System.Runtime.CompilerServices;

/// <summary>
/// No-op <see cref="IUiChannel"/> used when the pipeline runs headless (no UI connected).
/// All events are discarded; the request stream is always empty.
/// </summary>
internal sealed class NullUiChannel : IUiChannel
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly NullUiChannel Instance = new();

    private NullUiChannel() { }

    /// <inheritdoc/>
    public void SetSessionCorrelationId(Guid id) { /* headless — no wire frames to stamp */ }

    /// <inheritdoc/>
    public Task SendAsync(PipelineEvent evt, CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc/>
#pragma warning disable CS1998 // async method with no awaits — correct for empty async enumerable
    public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }
#pragma warning restore CS1998

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => default;
}
