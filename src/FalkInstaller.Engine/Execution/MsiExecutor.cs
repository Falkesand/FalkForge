namespace FalkInstaller.Engine.Execution;

using System.Diagnostics;
using System.Text.RegularExpressions;
using FalkInstaller.Engine.Planning;

public sealed partial class MsiExecutor
{
    private static readonly char[] ProhibitedValueChars = ['"', '&', '|', ';', '>', '<'];

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct)
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

        // Validate and add custom properties
        foreach (var prop in action.Properties)
        {
            if (!MsiPropertyKeyPattern().IsMatch(prop.Key))
                return Result<int>.Failure(
                    ErrorKind.SecurityError,
                    $"Invalid MSI property key '{prop.Key}': must match ^[A-Z_][A-Z0-9_.]*$");

            if (prop.Value.AsSpan().IndexOfAny(ProhibitedValueChars) >= 0)
                return Result<int>.Failure(
                    ErrorKind.SecurityError,
                    $"MSI property value for '{prop.Key}' contains prohibited characters");

            args += $" {prop.Key}=\"{prop.Value}\"";
        }

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
