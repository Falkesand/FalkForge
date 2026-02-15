namespace FalkInstaller.Engine.Journal.UndoOperations;

using FalkInstaller.Engine.Execution;

public sealed class ExeRollbackOperation : IUndoOperation
{
    private readonly IProcessRunner _processRunner;

    public ExeRollbackOperation(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public bool CanHandle(JournalEntry entry) => entry.EntryType == JournalEntryType.ExeInstalled;

    public async Task<Result<Unit>> ExecuteAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.EntryType != JournalEntryType.ExeInstalled)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"ExeRollbackOperation cannot handle entry type {entry.EntryType}");
        }

        var uninstallCommand = entry.UninstallCommand;
        if (string.IsNullOrWhiteSpace(uninstallCommand))
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"No uninstall command available for EXE package '{entry.PackageId}'; rollback skipped");
        }

        var (fileName, arguments) = ParseCommand(uninstallCommand);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"Invalid uninstall command for EXE package '{entry.PackageId}': empty executable path");
        }

        if (!Path.IsPathFullyQualified(fileName))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"Uninstall executable path must be fully qualified, got: '{fileName}'");
        }

        if (!File.Exists(fileName))
        {
            return Result<Unit>.Failure(ErrorKind.FileNotFound,
                $"Uninstall executable not found: '{fileName}'");
        }

        try
        {
            var exitCode = await _processRunner.RunAsync(fileName, arguments, ct);

            if (exitCode == 0)
            {
                return Unit.Value;
            }

            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"EXE uninstall for '{entry.PackageId}' failed with exit code {exitCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"EXE uninstall for '{entry.PackageId}' failed: {ex.Message}");
        }
    }

    public static (string FileName, string Arguments) ParseCommand(string command)
    {
        var trimmed = command.Trim();

        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                var fileName = trimmed[1..closingQuote];
                var arguments = closingQuote + 1 < trimmed.Length
                    ? trimmed[(closingQuote + 1)..].TrimStart()
                    : string.Empty;
                return (fileName, arguments);
            }
        }

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return (trimmed[..spaceIndex], trimmed[(spaceIndex + 1)..].TrimStart());
        }

        return (trimmed, string.Empty);
    }
}
