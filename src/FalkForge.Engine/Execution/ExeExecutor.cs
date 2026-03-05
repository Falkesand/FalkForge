namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;

internal sealed class ExeExecutor(IProcessRunner processRunner)
{
    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        var key = action.ActionType switch
        {
            PlanActionType.Install => "InstallArguments",
            PlanActionType.Uninstall => "UninstallArguments",
            PlanActionType.Repair => "RepairArguments",
            _ => (string?)null
        };

        if (key is null)
            return Result<int>.Failure(ErrorKind.ExecutionError, $"Unsupported action type: {action.ActionType}");

        var arguments = action.Package.Properties.GetValueOrDefault(key, "");

        try
        {
            var exitCode = await processRunner.RunAsync(action.Package.SourcePath, arguments, ct);
            return exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(ErrorKind.ExecutionError, $"Failed to execute EXE package: {ex.Message}");
        }
    }
}
