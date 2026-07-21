namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;

public sealed class NetRuntimeExecutor(IProcessRunner processRunner)
{
    private const string DefaultInstallArgs = "/install /quiet /norestart";
    private const string DefaultUninstallArgs = "/uninstall /quiet /norestart";
    private const string DefaultRepairArgs = "/repair /quiet /norestart";

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        var arguments = action.ActionType switch
        {
            PlanActionType.Install => action.Package.Properties.GetValueOrDefault("InstallArguments", DefaultInstallArgs),
            PlanActionType.Uninstall => action.Package.Properties.GetValueOrDefault("UninstallArguments", DefaultUninstallArgs),
            PlanActionType.Repair => action.Package.Properties.GetValueOrDefault("RepairArguments", DefaultRepairArgs),
            _ => (string?)null
        };

        if (arguments is null)
            return Result<int>.Failure(ErrorKind.ExecutionError, $"Unsupported action type: {action.ActionType}");

        try
        {
            // EffectiveSourcePath resolves to the extracted payload when the bootstrapper forwarded a
            // payload root (distributed bundle); otherwise the manifest's build-authored SourcePath.
            var exitCode = await processRunner.RunAsync(action.EffectiveSourcePath, arguments, ct);
            return exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(ErrorKind.ExecutionError, $"Failed to execute .NET runtime installer: {ex.Message}");
        }
    }
}
