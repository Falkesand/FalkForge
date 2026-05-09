namespace FalkForge.Engine;

using FalkForge.Engine.Pipeline;

/// <summary>
/// Public facade for running the installer engine from a named pipe or a test channel.
/// Owns the lifetime of the pipeline, UI channel, logger, and journal store.
/// </summary>
// S3453: instantiation happens through the public static factory methods BindToPipe / BindToChannel.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Sonar", "S3453",
    Justification = "Factory pattern: BindToPipe and BindToChannel are the public entry points.")]
public sealed class EngineSession : IAsyncDisposable
{
    private readonly IUiChannel _channel;
    private readonly EngineSessionOptions _options;

    private EngineSession(IUiChannel channel, EngineSessionOptions options)
    {
        _channel = channel;
        _options = options;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Production entry point
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="EngineSession"/> that communicates with the UI process
    /// over a named pipe. This is the production entry point used by <c>Program.cs</c>.
    /// </summary>
    /// <param name="pipeName">Named pipe to connect to, or <c>null</c> for headless mode.</param>
    /// <param name="manifestPath">Path to the installer manifest JSON file.</param>
    /// <param name="options">Optional session configuration overrides.</param>
    public static EngineSession BindToPipe(
        string? pipeName,
        string manifestPath,
        EngineSessionOptions? options = null)
    {
        throw new NotSupportedException("EngineSession.BindToPipe — stub for RED test phase");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test-only entry point (InternalsVisibleTo FalkForge.Engine.Tests)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="EngineSession"/> backed by a caller-supplied
    /// <see cref="IUiChannel"/>. Intended for unit tests only — bypasses named-pipe
    /// setup and uses the provided channel directly.
    /// </summary>
    internal static EngineSession BindToChannel(
        IUiChannel channel,
        EngineSessionOptions? options = null)
    {
        throw new NotSupportedException("EngineSession.BindToChannel — stub for RED test phase");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Run
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the installer pipeline to completion and returns the terminal outcome.
    /// Blocks until the UI signals shutdown, the cancellation token fires, or a fatal
    /// error occurs.
    /// </summary>
    public Task<EngineOutcome> RunUntilShutdown(CancellationToken ct = default)
    {
        // Suppress unused-field warnings during the stub phase.
        _ = _channel;
        _ = _options;
        _ = ct;
        throw new NotSupportedException("EngineSession.RunUntilShutdown — stub for RED test phase");
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => default;
}
