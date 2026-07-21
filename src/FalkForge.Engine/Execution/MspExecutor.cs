namespace FalkForge.Engine.Execution;

using System.Text.RegularExpressions;
using FalkForge.Engine.Planning;

public sealed partial class MspExecutor
{
    private const int SuccessExitCode = 0;
    private const int RebootRequiredExitCode = 3010;
    private const int CancelledExitCode = 1602;

    private readonly IProcessRunner _processRunner;

    public MspExecutor(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        var args = BuildArguments(action);
        if (args.IsFailure)
            return Result<int>.Failure(args.Error);

        try
        {
            var exitCode = await _processRunner.RunAsync("msiexec.exe", args.Value, ct);
            var mapped = MapExitCode(exitCode);
            return mapped.IsFailure
                ? Result<int>.Failure(mapped.Error)
                : exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Failed to execute MSP: {ex.Message}");
        }
    }

    internal static Result<string> BuildArguments(PlanAction action)
    {
        // The install arguments are built as a raw string; an embedded quote in the
        // build-time-authored SourcePath would break out of the quoting and inject extra
        // msiexec switches. Mirrors the MsuExecutor guard (defense-in-depth consistency).
        // EffectiveSourcePath resolves to the extracted payload when the bootstrapper forwarded a
        // payload root (distributed bundle); otherwise the manifest's build-authored SourcePath.
        if (action.EffectiveSourcePath.Contains('"'))
            return Result<string>.Failure(
                ErrorKind.Validation, "MSP SourcePath contains embedded quotes, which is not allowed.");

        return action.ActionType switch
        {
            PlanActionType.Install => Result<string>.Success(
                $"/p \"{action.EffectiveSourcePath}\" /quiet /norestart"),
            PlanActionType.Uninstall => BuildUninstallArguments(action),
            _ => Result<string>.Failure(
                ErrorKind.ExecutionError, $"Unsupported action type for MSP package: {action.ActionType}")
        };
    }

    private static Result<string> BuildUninstallArguments(PlanAction action)
    {
        var targetProductCode = action.Package.TargetProductCode;
        var patchCode = action.Package.PatchCode;

        if (string.IsNullOrEmpty(targetProductCode))
        {
            return Result<string>.Failure(
                ErrorKind.ExecutionError, "TargetProductCode is required for MSP uninstall");
        }

        if (string.IsNullOrEmpty(patchCode))
        {
            return Result<string>.Failure(
                ErrorKind.ExecutionError, "PatchCode is required for MSP uninstall");
        }

        if (!IsValidGuid(targetProductCode))
        {
            return Result<string>.Failure(
                ErrorKind.Validation, "Invalid TargetProductCode format: expected a GUID");
        }

        if (!IsValidGuid(patchCode))
        {
            return Result<string>.Failure(
                ErrorKind.Validation, "Invalid PatchCode format: expected a GUID");
        }

        return Result<string>.Success(
            $"/i \"{targetProductCode}\" MSIPATCHREMOVE=\"{patchCode}\" /quiet /norestart");
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex StrictGuidRegex();

    private static bool IsValidGuid(string value)
    {
        return StrictGuidRegex().IsMatch(value);
    }

    internal static Result<Unit> MapExitCode(int exitCode)
    {
        return exitCode switch
        {
            SuccessExitCode or RebootRequiredExitCode => Unit.Value,
            CancelledExitCode => Result<Unit>.Failure(
                ErrorKind.ExecutionError, "MSP installation was cancelled by the user"),
            _ => Result<Unit>.Failure(
                ErrorKind.ExecutionError, $"msiexec.exe exited with code {exitCode}")
        };
    }
}
