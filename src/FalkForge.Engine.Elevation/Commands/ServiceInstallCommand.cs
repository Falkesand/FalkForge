namespace FalkForge.Engine.Elevation.Commands;

using System.Diagnostics;
using System.Text.RegularExpressions;

public sealed partial class ServiceInstallCommand : IElevatedCommand
{
    private const int ProcessTimeoutMs = 600_000;
    private static readonly char[] ShellMetacharacters = ['&', '|', ';', '>', '<', '`', '$', '(', ')', '{', '}', '"'];

    public string Name => "ServiceInstall";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var serviceName = reader.ReadString();
        var displayName = reader.ReadString();
        var binaryPath = reader.ReadString();

        if (!ServiceNamePattern().IsMatch(serviceName))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Service name must contain only alphanumeric characters, underscores, and dashes");

        if (displayName.AsSpan().IndexOfAny(ShellMetacharacters) >= 0)
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Display name contains prohibited shell metacharacters");

        string normalizedBinaryPath;
        try
        {
            normalizedBinaryPath = Path.GetFullPath(binaryPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Result<byte[]>.Failure(ErrorKind.SecurityError, $"Invalid binary path: {ex.Message}");
        }

        if (normalizedBinaryPath.Contains("..", StringComparison.Ordinal))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Binary path must not contain '..' segments after normalization");

        if (!FileWriteCommand.IsAllowedPath(normalizedBinaryPath))
            return Result<byte[]>.Failure(ErrorKind.SecurityError,
                "Binary path must be under Program Files or ProgramData");

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create \"{serviceName}\" DisplayName= \"{displayName}\" binPath= \"{normalizedBinaryPath}\" start= auto",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                process.Kill(entireProcessTree: true);
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, "sc.exe timed out and was terminated");
            }

            if (process.ExitCode != 0)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"sc.exe exited with code {process.ExitCode}");

            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"Service install failed: {ex.Message}");
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex ServiceNamePattern();
}
