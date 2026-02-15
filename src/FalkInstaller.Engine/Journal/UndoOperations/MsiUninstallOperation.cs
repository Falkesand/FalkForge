namespace FalkInstaller.Engine.Journal.UndoOperations;

using System.Text.RegularExpressions;
using FalkInstaller.Engine.Execution;

public sealed partial class MsiUninstallOperation : IUndoOperation
{
    private readonly IProcessRunner _processRunner;

    public MsiUninstallOperation(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public bool CanHandle(JournalEntry entry) => entry.EntryType == JournalEntryType.MsiInstalled;

    public async Task<Result<Unit>> ExecuteAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.EntryType != JournalEntryType.MsiInstalled)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"MsiUninstallOperation cannot handle entry type {entry.EntryType}");
        }

        var productCode = entry.ProductCode;
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"ProductCode is required for MSI rollback of package '{entry.PackageId}'");
        }

        if (!GuidPattern().IsMatch(productCode))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"Invalid ProductCode format '{productCode}': expected a GUID in braces (e.g., {{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}})");
        }

        try
        {
            var arguments = $"/x {productCode} /qn /norestart";
            var exitCode = await _processRunner.RunAsync("msiexec.exe", arguments, ct);

            return exitCode switch
            {
                0 => Unit.Value,                    // ERROR_SUCCESS
                1605 => Unit.Value,                  // ERROR_UNKNOWN_PRODUCT (already removed)
                1641 => Unit.Value,                  // ERROR_SUCCESS_REBOOT_INITIATED
                3010 => Unit.Value,                  // ERROR_SUCCESS_REBOOT_REQUIRED
                _ => Result<Unit>.Failure(ErrorKind.RollbackError,
                    $"MSI uninstall of {productCode} failed with exit code {exitCode}")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<Unit>.Failure(ErrorKind.RollbackError,
                $"MSI uninstall of {productCode} failed: {ex.Message}");
        }
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex GuidPattern();
}
