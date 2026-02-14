namespace FalkInstaller.Engine.Elevation.Commands;

using System.Diagnostics;

public sealed class MsiInstallCommand : IElevatedCommand
{
    private const int ProcessTimeoutMs = 600_000;
    private static readonly char[] ShellMetacharacters = ['&', '|', ';', '>', '<', '`', '$', '(', ')', '{', '}'];

    public string Name => "MsiInstall";

    public Result<byte[]> Execute(byte[] payload)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();

        if (!File.Exists(msiPath))
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI file not found: {msiPath}");

        if (additionalArgs.AsSpan().IndexOfAny(ShellMetacharacters) >= 0)
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Additional arguments contain prohibited shell metacharacters");

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /qn /norestart {additionalArgs}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, "msiexec timed out and was terminated");
            }

            if (process.ExitCode != 0)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"msiexec exited with code {process.ExitCode}");

            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI install failed: {ex.Message}");
        }
    }
}
