namespace FalkForge.Engine.Execution;

using System.Diagnostics;
using System.Text.RegularExpressions;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Planning;

public sealed partial class MsiExecutor
{
    private static readonly char[] ProhibitedValueChars = ['"', '&', '|', ';', '>', '<'];

    private readonly Func<IElevationClient?> _elevationClientAccessor;

    public MsiExecutor()
    {
        _elevationClientAccessor = static () => null;
    }

    public MsiExecutor(Func<IElevationClient?> elevationClientAccessor)
    {
        _elevationClientAccessor = elevationClientAccessor;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct)
    {
        // Validate custom properties up front (applies to both elevated and direct paths)
        var propsResult = ValidateAndBuildPropertyArgs(action);
        if (propsResult.IsFailure)
            return Result<int>.Failure(propsResult.Error);

        var elevationClient = _elevationClientAccessor();
        if (elevationClient is not null)
        {
            return await ExecuteElevatedAsync(action, propsResult.Value, elevationClient, ct);
        }

        return await ExecuteDirectAsync(action, propsResult.Value, ct);
    }

    private static Result<string> ValidateAndBuildPropertyArgs(PlanAction action)
    {
        var additionalArgs = string.Empty;

        foreach (var prop in action.Properties)
        {
            if (!MsiPropertyKeyPattern().IsMatch(prop.Key))
                return Result<string>.Failure(
                    ErrorKind.SecurityError,
                    $"Invalid MSI property key '{prop.Key}': must match ^[A-Z_][A-Z0-9_.]*$");

            if (prop.Value.AsSpan().IndexOfAny(ProhibitedValueChars) >= 0)
                return Result<string>.Failure(
                    ErrorKind.SecurityError,
                    $"MSI property value for '{prop.Key}' contains prohibited characters");

            additionalArgs += $" {prop.Key}=\"{prop.Value}\"";
        }

        return additionalArgs;
    }

    private static async Task<Result<int>> ExecuteElevatedAsync(
        PlanAction action,
        string additionalArgs,
        IElevationClient elevationClient,
        CancellationToken ct)
    {
        try
        {
            string commandName;
            byte[] payload;

            if (action.ActionType is PlanActionType.Uninstall)
            {
                // MsiUninstallCommand expects: productCode (string) via BinaryWriter
                var productCode = action.Package.Properties.GetValueOrDefault("ProductCode")
                                  ?? action.Package.SourcePath;
                commandName = "MsiUninstall";
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(productCode);
                }
                payload = stream.ToArray();
            }
            else
            {
                // MsiInstallCommand expects: msiPath (string) + additionalArgs (string) via BinaryWriter
                commandName = "MsiInstall";
                using var stream = new MemoryStream();
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(action.Package.SourcePath);
                    writer.Write(additionalArgs);
                }
                payload = stream.ToArray();
            }

            var result = await elevationClient.SendCommandAsync(commandName, payload, ct);
            if (result.IsFailure)
            {
                return Result<int>.Failure(ErrorKind.ExecutionError, result.Error.Message);
            }

            // Elevated command succeeded — exit code 0
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Elevated MSI execution failed: {ex.Message}");
        }
    }

    private static async Task<Result<int>> ExecuteDirectAsync(
        PlanAction action,
        string additionalArgs,
        CancellationToken ct)
    {
        var args = action.ActionType switch
        {
            PlanActionType.Install => $"/i \"{action.Package.SourcePath}\" /qn /norestart",
            PlanActionType.Uninstall => $"/x \"{action.Package.SourcePath}\" /qn /norestart",
            PlanActionType.Repair => $"/fa \"{action.Package.SourcePath}\" /qn /norestart",
            _ => (string?)null
        };

        if (args is null)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Unknown action type: {action.ActionType}");
        }

        args += additionalArgs;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync(ct);

            return process.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Failed to execute MSI: {ex.Message}");
        }
    }

    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$")]
    private static partial Regex MsiPropertyKeyPattern();
}
