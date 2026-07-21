namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;
using FalkForge.Engine.Variables;

public sealed class ExeExecutor
{
    private readonly IProcessRunner _processRunner;
    private readonly Func<VariableStore?> _variableStoreAccessor;

    public ExeExecutor(IProcessRunner processRunner)
        : this(processRunner, static () => null)
    {
    }

    public ExeExecutor(IProcessRunner processRunner, Func<VariableStore?> variableStoreAccessor)
    {
        _processRunner = processRunner;
        _variableStoreAccessor = variableStoreAccessor;
    }

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
        arguments = VariableResolver.Resolve(arguments, _variableStoreAccessor());

        try
        {
            // EffectiveSourcePath resolves to the extracted payload when the bootstrapper forwarded a
            // payload root (distributed bundle); otherwise the manifest's build-authored SourcePath.
            var exitCode = await _processRunner.RunAsync(action.EffectiveSourcePath, arguments, ct);
            return exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(ErrorKind.ExecutionError, $"Failed to execute EXE package: {ex.Message}");
        }
    }
}
