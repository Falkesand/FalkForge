namespace FalkInstaller.Cli;

/// <summary>
/// Maps <see cref="Result{T}"/> outcomes to CLI exit codes.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int ValidationFailure = 1;
    public const int CompilationError = 2;
    public const int RuntimeError = 3;

    /// <summary>
    /// Maps an <see cref="ErrorKind"/> to the appropriate CLI exit code.
    /// </summary>
    public static int FromErrorKind(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => ValidationFailure,
        ErrorKind.CompilationError => CompilationError,
        ErrorKind.FileNotFound => RuntimeError,
        ErrorKind.DirectoryNotFound => RuntimeError,
        ErrorKind.IoError => RuntimeError,
        ErrorKind.SecurityError => RuntimeError,
        ErrorKind.PlatformError => RuntimeError,
        ErrorKind.InvalidConfiguration => ValidationFailure,
        ErrorKind.InvalidOperation => RuntimeError,
        ErrorKind.NotSupported => RuntimeError,
        _ => RuntimeError
    };

    /// <summary>
    /// Maps a <see cref="Result{T}"/> to an exit code. Returns <see cref="Success"/> for successful results.
    /// </summary>
    public static int FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Success : FromErrorKind(result.Error.Kind);
}
