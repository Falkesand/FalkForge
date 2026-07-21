namespace FalkForge.Engine.Execution;

using System.Diagnostics;
using FalkForge.Engine.Planning;

public sealed class BundleExecutor
{
    private const int SuccessExitCode = 0;
    private const int RebootRequiredExitCode = 3010;

    private readonly IProcessRunner _processRunner;
    private readonly string? _allowedBasePath;

    /// <param name="processRunner">Spawns the nested bundle's exe.</param>
    /// <param name="allowedBasePath">
    /// Containment root checked against <see cref="Planning.PlanAction.EffectiveSourcePath"/> — the
    /// caller must supply the SAME root the caller resolved that path under (e.g. the bootstrapper's
    /// payload extraction root when one was forwarded), or the guard rejects a legitimately resolved
    /// path as "outside the allowed cache directory". Null disables the check (offline/plan paths
    /// where SourcePath is manifest-authoritative and no single root applies).
    /// </param>
    public BundleExecutor(IProcessRunner processRunner, string? allowedBasePath = null)
    {
        _processRunner = processRunner;
        _allowedBasePath = allowedBasePath;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        var args = BuildArguments(action);
        if (args.IsFailure)
            return Result<int>.Failure(args.Error);

        // EffectiveSourcePath resolves to the extracted payload when the bootstrapper forwarded a
        // payload root (distributed bundle); otherwise the manifest's build-authored SourcePath.
        var validationResult = ValidateSourcePath(action.EffectiveSourcePath);
        if (validationResult.IsFailure)
            return Result<int>.Failure(validationResult.Error);

        try
        {
            var processId = 0;
            using var cancellationRegistration = ct.Register(() =>
            {
                if (processId > 0)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(processId);
                        proc.Kill(entireProcessTree: true);
                    }
                    catch (ArgumentException)
                    {
                        // Process already exited
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited
                    }
                }
            });

            var exitCode = await _processRunner.RunAsync(
                action.EffectiveSourcePath,
                args.Value,
                pid => processId = pid,
                ct);

            var mapped = MapExitCode(exitCode);
            return mapped.IsFailure
                ? Result<int>.Failure(mapped.Error)
                : exitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Failed to execute bundle: {ex.Message}");
        }
    }

    internal static Result<string> BuildArguments(PlanAction action)
    {
        return action.ActionType switch
        {
            PlanActionType.Install => Result<string>.Success("/quiet /norestart"),
            PlanActionType.Uninstall => Result<string>.Success("/quiet /norestart /uninstall"),
            PlanActionType.Repair => Result<string>.Success("/quiet /norestart /repair"),
            _ => Result<string>.Failure(
                ErrorKind.ExecutionError, $"Unsupported action type for bundle package: {action.ActionType}")
        };
    }

    internal static Result<Unit> MapExitCode(int exitCode)
    {
        return exitCode switch
        {
            SuccessExitCode or RebootRequiredExitCode => Unit.Value,
            _ => Result<Unit>.Failure(
                ErrorKind.ExecutionError, $"Bundle exited with code {exitCode}")
        };
    }

    internal Result<Unit> ValidateSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return Result<Unit>.Failure(
                ErrorKind.ExecutionError, "Bundle source path is empty.");

        if (!string.Equals(Path.GetExtension(sourcePath), ".exe", StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(
                ErrorKind.ExecutionError, $"Bundle source path must have .exe extension, got '{Path.GetExtension(sourcePath)}'.");

        if (_allowedBasePath is not null)
        {
            var resolvedPath = Path.GetFullPath(sourcePath);
            var resolvedBase = Path.GetFullPath(_allowedBasePath + Path.DirectorySeparatorChar);

            if (!resolvedPath.StartsWith(resolvedBase, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(
                    ErrorKind.ExecutionError,
                    "Bundle source path resolves outside the allowed cache directory.");
        }

        return Unit.Value;
    }
}
