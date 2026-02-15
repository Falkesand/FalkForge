namespace FalkForge.Engine.Elevation.Commands;

using System.Diagnostics;
using System.Text.RegularExpressions;

public sealed partial class MsiUninstallCommand : IElevatedCommand
{
    private const int ProcessTimeoutMs = 600_000;

    public string Name => "MsiUninstall";

    public Result<byte[]> Execute(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        var productCode = reader.ReadString();

        if (!GuidPattern().IsMatch(productCode))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Product code must be a valid GUID in the format {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}");

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/x \"{productCode}\" /qn /norestart",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, "msiexec uninstall timed out and was terminated");
            }

            if (process.ExitCode != 0)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"msiexec uninstall exited with code {process.ExitCode}");

            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI uninstall failed: {ex.Message}");
        }
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex GuidPattern();
}
