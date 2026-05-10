namespace FalkForge.Engine;

using FalkForge.Engine.Protocol;

/// <summary>
/// Parsed command-line flags relevant to engine startup. Currently models only the
/// runtime-logging flags (<c>--log</c>, <c>--log-level</c>) since other flags continue
/// to be parsed inline in <c>Program.Main</c> for backward compatibility.
/// </summary>
internal sealed record ProgramArgs(
    string? LogPath,
    LogLevel? MinimumLogLevel)
{
    /// <summary>
    /// Result returned by <see cref="Parse(string[])"/>. Carries either parsed
    /// <see cref="ProgramArgs"/> on success or a human-readable error message and
    /// suggested process exit code on failure.
    /// </summary>
    internal readonly struct Result
    {
        private Result(bool success, ProgramArgs? value, string? errorMessage, int suggestedExitCode)
        {
            IsSuccess = success;
            _value = value;
            ErrorMessage = errorMessage;
            SuggestedExitCode = suggestedExitCode;
        }

        private readonly ProgramArgs? _value;

        public bool IsSuccess { get; }
        public ProgramArgs Value => _value ?? throw new InvalidOperationException("Result has no value (Failure).");
        public string? ErrorMessage { get; }
        public int SuggestedExitCode { get; }

        public static Result Success(ProgramArgs value) => new(true, value, null, 0);
        public static Result Failure(string message, int exitCode = 1) => new(false, null, message, exitCode);
    }

    /// <summary>
    /// Parses the supported logging flags from <paramref name="args"/>. Unknown flags are
    /// ignored (for compatibility with <c>Program.Main</c>'s own argument loop).
    /// </summary>
    /// <remarks>
    /// Recognised flags (all optional):
    /// <list type="bullet">
    /// <item><c>--log &lt;path&gt;</c> / <c>/log &lt;path&gt;</c> / <c>/L &lt;path&gt;</c> — log file path.</item>
    /// <item><c>--log-level &lt;level&gt;</c> / <c>/lv &lt;level&gt;</c> — minimum log level (case-insensitive).</item>
    /// </list>
    /// </remarks>
    public static Result Parse(string[] args)
    {
        string? rawLogPath = null;
        LogLevel? logLevel = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--log":
                case "/log":
                case "/L":
                    if (i + 1 >= args.Length)
                        return Result.Failure($"Missing value for '{a}'.");
                    rawLogPath = args[++i];
                    break;

                case "--log-level":
                case "/lv":
                    if (i + 1 >= args.Length)
                        return Result.Failure($"Missing value for '{a}'.");
                    var raw = args[++i];
                    if (!Enum.TryParse<LogLevel>(raw, ignoreCase: true, out var parsed))
                        return Result.Failure(
                            $"Invalid log level '{raw}'. Valid: Verbose, Debug, Info, Warning, Error.");
                    logLevel = parsed;
                    break;

                default:
                    // Skip values that follow known value-bearing flags so they aren't re-parsed.
                    break;
            }
        }

        // Resolve / validate the log path if one was supplied.
        string? resolvedLogPath = null;
        if (rawLogPath is not null)
        {
            // SECURITY: reject literal traversal segments before any normalisation.
            // Path.GetFullPath would silently collapse "C:\Logs\..\..\Windows\foo.log" into
            // "C:\Windows\foo.log" — accept only paths the user actually intended.
            if (ContainsTraversalSegment(rawLogPath))
                return Result.Failure(
                    $"Log path '{rawLogPath}' contains '..' segments which are not permitted.");

            // If the supplied path resolves to an existing directory, append the default file name.
            var resolvedPath = rawLogPath;
            if (Directory.Exists(rawLogPath))
                resolvedPath = Path.Combine(rawLogPath, "engine.log");

            resolvedLogPath = resolvedPath;
        }

        return Result.Success(new ProgramArgs(resolvedLogPath, logLevel));
    }

    private static bool ContainsTraversalSegment(string path)
    {
        // Split on both Windows and POSIX separators so the check is robust on either OS.
        foreach (var segment in path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
                return true;
        }

        return false;
    }
}
