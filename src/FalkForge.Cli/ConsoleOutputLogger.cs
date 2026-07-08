using FalkForge.Diagnostics;
using Spectre.Console;

namespace FalkForge.Cli;

/// <summary>
/// <see cref="IFalkLogger"/> adapter that routes structured log entries through the CLI's
/// existing <see cref="IConsoleOutput"/> abstraction, so build-time logging (Compiler.Msi
/// pipeline steps, warnings for the previously-swallowed SBOM/ICE failures, etc.) shows up
/// consistently whether the command is running in interactive Spectre mode or <c>--json</c>
/// mode. Deliberately depends only on <c>FalkForge.Diagnostics</c> (Core) and the CLI's own
/// <see cref="IConsoleOutput"/> -- no reference to <c>FalkForge.Engine</c>.
/// </summary>
/// <remarks>
/// Level routing: <see cref="LogLevel.Error"/> goes to <see cref="IConsoleOutput.WriteError"/>
/// (red, always visible); <see cref="LogLevel.Warning"/> uses a yellow <c>MarkupLine</c> (matches
/// the existing "[yellow]Warning:[/] ..." convention already used by <c>BuildCommand</c>) so the
/// severity survives into the <c>--json</c> envelope as <c>"warning"</c> rather than being folded
/// into <c>"error"</c>; <see cref="LogLevel.Info"/> is a normal <c>WriteLine</c>;
/// <see cref="LogLevel.Debug"/> and <see cref="LogLevel.Verbose"/> render as grey <c>MarkupLine</c>
/// entries but are only ever reached when <see cref="MinimumLevel"/> permits it -- gated behind
/// the constructor's <c>verbose</c> flag (CLI's <c>--verbose</c> option).
/// </remarks>
public sealed class ConsoleOutputLogger : IFalkLogger
{
    private readonly IConsoleOutput _console;

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; set; }

    /// <inheritdoc/>
    public Guid SessionCorrelationId { get; set; }

    /// <summary>
    /// Creates the adapter. <paramref name="verbose"/> maps directly to the CLI's
    /// <c>--verbose</c> flag: when <see langword="false"/>, <see cref="LogLevel.Debug"/> and
    /// <see cref="LogLevel.Verbose"/> entries are discarded before any allocation (D2/D6 -- the
    /// interface's own <see cref="MinimumLevel"/> gate handles this, no extra check needed here).
    /// </summary>
    public ConsoleOutputLogger(IConsoleOutput console, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(console);
        _console = console;
        MinimumLevel = verbose ? LogLevel.Debug : LogLevel.Info;
    }

    /// <inheritdoc/>
    public void SetMinimumLevel(LogLevel level) => MinimumLevel = level;

    /// <inheritdoc/>
    public void Log(LogLevel level, string category, string message, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (level < MinimumLevel)
            return;

        switch (level)
        {
            case LogLevel.Error:
                _console.WriteError($"{category}: {message}");
                break;
            case LogLevel.Warning:
                _console.MarkupLine($"[yellow]{Markup.Escape(category)}:[/] {Markup.Escape(message)}");
                break;
            case LogLevel.Info:
                _console.WriteLine($"{category}: {message}");
                break;
            default: // Debug, Verbose
                _console.MarkupLine($"[grey]{Markup.Escape(category)}:[/] {Markup.Escape(message)}");
                break;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Folds the exception's type and message into the rendered line so the diagnostic trail
    /// is not lost to a plain <c>ex.Message</c>-only interpolation. The full structured
    /// exception properties (type/message/stackTrace) are still available via
    /// <see cref="LogProperties.MergeException"/> for sinks that consume <see cref="LogEntry"/>
    /// directly (e.g. <c>ListLogger</c> in tests); the console line stays short.
    /// </remarks>
    public void Log(LogLevel level, string category, string message, Exception? exception, IReadOnlyDictionary<string, string>? properties = null)
    {
        if (exception is null)
        {
            Log(level, category, message, properties);
            return;
        }

        Log(level, category, $"{message} ({exception.GetType().Name}: {exception.Message})", properties);
    }

    /// <inheritdoc/>
    public void Verbose(string category, string message) => Log(LogLevel.Verbose, category, message);

    /// <inheritdoc/>
    public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);

    /// <inheritdoc/>
    public void Info(string category, string message) => Log(LogLevel.Info, category, message);

    /// <inheritdoc/>
    public void Warning(string category, string message) => Log(LogLevel.Warning, category, message);

    /// <inheritdoc/>
    public void Error(string category, string message) => Log(LogLevel.Error, category, message);

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing to dispose; the underlying IConsoleOutput is owned by the command.
    }
}
