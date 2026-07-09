using FalkForge.Diagnostics;
using FalkForge.Engine.Pipeline;

namespace FalkForge.Engine;

/// <summary>
/// Fans log entries out to a UI channel that is bound <em>after</em> the logger has been
/// constructed. Solves the chicken-and-egg between logger construction (must happen first
/// so manifest-load failures still surface to a logger) and channel construction: the
/// forwarder is wired into <see cref="EngineLogger"/>'s pipe callback up front, and its
/// <see cref="Channel"/> is assigned once the channel exists.
/// </summary>
internal sealed class UiChannelLogForwarder
{
    /// <summary>
    /// The UI channel to forward log entries to. Assigned after the channel is created;
    /// <see cref="Dispatch"/> null-checks it so pre-bind log writes are safe no-ops.
    /// </summary>
    public IUiChannel? Channel { get; set; }

    /// <summary>
    /// Forwards a single <see cref="LogEntry"/> to the bound UI channel as a
    /// <see cref="PipelineEvent.Log"/>.  Fire-and-forget — dispatch hops onto the
    /// ThreadPool so the logger's call site never blocks on channel I/O.  All
    /// exceptions are swallowed: a failing channel must not crash the logger,
    /// nor recursively log via the same logger (would loop).
    /// </summary>
    /// <remarks>
    /// Trade-off: the entry record (<see cref="PipelineEvent.Log"/>) is one
    /// allocation per accepted log call. Acceptable: log emission is bounded by
    /// pipeline phase activity, not by hot-path tight loops, and the level
    /// filter inside <see cref="EngineLogger.Log"/> already short-circuits
    /// below-threshold entries before we reach this method.
    /// </remarks>
    public void Dispatch(LogEntry entry)
    {
        var channel = Channel;
        if (channel is null)
            return;

        // Map LogEntry → PipelineEvent.Log. NamedPipeUiChannel.TranslateEvent
        // already converts this into the wire-level LogMessage frame.
        var pipelineEvent = new PipelineEvent.Log(entry.Level, entry.Message);

        // Fire-and-forget on the ThreadPool. We do not await — blocking the
        // logger's call site on pipe I/O could stall pipeline progress.
        _ = Task.Run(async () =>
        {
            try
            {
                await channel.SendAsync(pipelineEvent, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Swallow. Re-logging here would recurse through the same callback.
            }
        });
    }
}
