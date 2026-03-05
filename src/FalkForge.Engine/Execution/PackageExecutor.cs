namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;

public sealed class PackageExecutor
{
    private readonly MsiExecutor _msiExecutor;
    private readonly MsuExecutor _msuExecutor;
    private readonly MspExecutor _mspExecutor;
    private readonly BundleExecutor _bundleExecutor;
    private readonly ExeExecutor _exeExecutor;
    private readonly NetRuntimeExecutor _netRuntimeExecutor;

    public PackageExecutor(
        MsiExecutor msiExecutor,
        MsuExecutor msuExecutor,
        MspExecutor mspExecutor,
        BundleExecutor bundleExecutor,
        ExeExecutor exeExecutor,
        NetRuntimeExecutor netRuntimeExecutor)
    {
        _msiExecutor = msiExecutor;
        _msuExecutor = msuExecutor;
        _mspExecutor = mspExecutor;
        _bundleExecutor = bundleExecutor;
        _exeExecutor = exeExecutor;
        _netRuntimeExecutor = netRuntimeExecutor;
    }

    /// <summary>
    /// Executes a package action. When <paramref name="isDryRun"/> is true, simulates the
    /// execution (returns success) and appends a log entry to <paramref name="dryRunLogPath"/>
    /// instead of invoking the real installer.
    /// </summary>
    public async Task<Result<ExecutionOutcome>> ExecuteAsync(
        PlanAction action,
        bool isDryRun,
        string? dryRunLogPath,
        CancellationToken ct,
        IProgress<int>? packageProgress = null)
    {
        if (isDryRun)
        {
            return await SimulateDryRunAsync(action, dryRunLogPath, ct);
        }

        var innerResult = action.Package.Type switch
        {
            PackageType.MsiPackage => await _msiExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.MsuPackage => await _msuExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.MspPackage => await _mspExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.BundlePackage => await _bundleExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.ExePackage => await _exeExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.NetRuntime => await _netRuntimeExecutor.ExecuteAsync(action, ct, packageProgress),
            _ => Result<int>.Failure(
                ErrorKind.ExecutionError, $"Unknown package type: {action.Package.Type}")
        };

        if (innerResult.IsFailure)
        {
            return Result<ExecutionOutcome>.Failure(innerResult.Error);
        }

        return MapExitCode(action, innerResult.Value);
    }

    /// <summary>
    /// Overload kept for backwards-compatibility; defaults to no dry-run.
    /// </summary>
    public Task<Result<ExecutionOutcome>> ExecuteAsync(PlanAction action, CancellationToken ct) =>
        ExecuteAsync(action, isDryRun: false, dryRunLogPath: null, ct);

    private static async Task<Result<ExecutionOutcome>> SimulateDryRunAsync(
        PlanAction action,
        string? logPath,
        CancellationToken ct)
    {
        var logLine = string.Concat(
            "[", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "] ",
            "DRY RUN: Would ", action.ActionType.ToString().ToUpperInvariant(),
            " ", action.Package.Type, " package '", action.PackageId, "'",
            " (", action.Package.DisplayName ?? action.PackageId, ")");

        if (!string.IsNullOrEmpty(logPath))
        {
            try
            {
                await File.AppendAllTextAsync(logPath, logLine + Environment.NewLine, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log write failure is non-fatal for dry-run; continue simulating
            }
        }

        return ExecutionOutcome.Success;
    }

    public Result<ExecutionOutcome> MapExitCode(PlanAction action, int processExitCode)
    {
        var mapping = ExitCodeMapping.FromDictionary(action.Package.ExitCodes);
        var behavior = mapping.Map(processExitCode);

        return behavior switch
        {
            ExitCodeBehavior.Success => ExecutionOutcome.Success,
            ExitCodeBehavior.RebootRequired => ExecutionOutcome.RebootRequired,
            ExitCodeBehavior.ScheduleReboot => ExecutionOutcome.ScheduleReboot,
            ExitCodeBehavior.Failure => Result<ExecutionOutcome>.Failure(
                ErrorKind.ExecutionError,
                $"Package '{action.PackageId}' failed with exit code {processExitCode}"),
            _ => Result<ExecutionOutcome>.Failure(
                ErrorKind.ExecutionError,
                $"Package '{action.PackageId}' returned unknown behavior for exit code {processExitCode}")
        };
    }
}
