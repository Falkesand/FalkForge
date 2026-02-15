namespace FalkForge.Engine.Execution;

using FalkForge.Engine.Planning;

public sealed class MsuExecutor
{
    private const int SuccessExitCode = 0;
    private const int RebootRequiredExitCode = 3010;
    private const int AlreadyInstalledExitCode = 2359302;

    private readonly IProcessRunner _processRunner;

    public MsuExecutor(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct)
    {
        var args = BuildArguments(action);
        if (args.IsFailure)
            return Result<int>.Failure(args.Error);

        try
        {
            var exitCode = await _processRunner.RunAsync("wusa.exe", args.Value, ct);
            var mapped = MapExitCode(exitCode);
            return mapped.IsFailure
                ? Result<int>.Failure(mapped.Error)
                : exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Failed to execute MSU: {ex.Message}");
        }
    }

    internal static Result<string> BuildArguments(PlanAction action)
    {
        return action.ActionType switch
        {
            PlanActionType.Install => Result<string>.Success(
                $"\"{action.Package.SourcePath}\" /quiet /norestart"),
            PlanActionType.Uninstall => BuildUninstallArguments(action),
            _ => Result<string>.Failure(
                ErrorKind.ExecutionError, $"Unsupported action type for MSU package: {action.ActionType}")
        };
    }

    private static Result<string> BuildUninstallArguments(PlanAction action)
    {
        var kbArticle = action.Package.KbArticle;
        if (string.IsNullOrEmpty(kbArticle))
        {
            return Result<string>.Failure(
                ErrorKind.ExecutionError, "KbArticle is required for MSU uninstall");
        }

        if (!kbArticle.All(char.IsDigit))
        {
            return Result<string>.Failure(
                ErrorKind.Validation, "Invalid KbArticle format: expected digits only");
        }

        return Result<string>.Success($"/uninstall /kb:{kbArticle} /quiet /norestart");
    }

    internal static Result<Unit> MapExitCode(int exitCode)
    {
        return exitCode switch
        {
            SuccessExitCode or RebootRequiredExitCode or AlreadyInstalledExitCode => Unit.Value,
            _ => Result<Unit>.Failure(
                ErrorKind.ExecutionError, $"wusa.exe exited with code {exitCode}")
        };
    }
}
