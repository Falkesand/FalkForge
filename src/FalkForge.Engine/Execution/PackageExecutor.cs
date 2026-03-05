namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;

public sealed class PackageExecutor
{
    private readonly MsiExecutor _msiExecutor;
    private readonly MsuExecutor _msuExecutor;
    private readonly MspExecutor _mspExecutor;
    private readonly BundleExecutor _bundleExecutor;

    public PackageExecutor(
        MsiExecutor msiExecutor,
        MsuExecutor msuExecutor,
        MspExecutor mspExecutor,
        BundleExecutor bundleExecutor)
    {
        _msiExecutor = msiExecutor;
        _msuExecutor = msuExecutor;
        _mspExecutor = mspExecutor;
        _bundleExecutor = bundleExecutor;
    }

    public async Task<Result<ExecutionOutcome>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        var innerResult = action.Package.Type switch
        {
            PackageType.MsiPackage => await _msiExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.MsuPackage => await _msuExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.MspPackage => await _mspExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.BundlePackage => await _bundleExecutor.ExecuteAsync(action, ct, packageProgress),
            PackageType.ExePackage => Result<int>.Failure(
                ErrorKind.ExecutionError, "EXE package execution not yet implemented"),
            PackageType.NetRuntime => Result<int>.Failure(
                ErrorKind.ExecutionError, ".NET runtime installation not yet implemented"),
            _ => Result<int>.Failure(
                ErrorKind.ExecutionError, $"Unknown package type: {action.Package.Type}")
        };

        if (innerResult.IsFailure)
        {
            return Result<ExecutionOutcome>.Failure(innerResult.Error);
        }

        return MapExitCode(action, innerResult.Value);
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
