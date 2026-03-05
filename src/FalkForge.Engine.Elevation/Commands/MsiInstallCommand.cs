namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.InteropServices;
using FalkForge.Platform.Windows;

public sealed class MsiInstallCommand : IElevatedCommand
{
    private const int InstallUILevelNone = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorSuccessRebootRequired = 3010;
    private static readonly char[] ShellMetacharacters = ['&', '|', ';', '>', '<', '`', '$', '(', ')', '{', '}'];

    private readonly IMsiApi _msiApi;

    public MsiInstallCommand(IMsiApi msiApi)
    {
        _msiApi = msiApi;
    }

    public string Name => "MsiInstall";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var msiPath = reader.ReadString();
        var additionalArgs = reader.ReadString();

        if (msiPath.StartsWith(@"\\", StringComparison.Ordinal))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "UNC/network MSI paths are not allowed");

        if (!File.Exists(msiPath))
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI file not found: {msiPath}");

        if (additionalArgs.AsSpan().IndexOfAny(ShellMetacharacters) >= 0)
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Additional arguments contain prohibited shell metacharacters");

        MsiExternalUIHandler? handler = null;
        GCHandle gcHandle = default;

        if (onProgress is not null)
        {
            var progressState = new MsiProgressState();
            handler = (context, messageType, message) =>
            {
                var percent = progressState.ProcessMessage(messageType, message);
                if (percent >= 0)
                    onProgress(percent);
                return 0;
            };
            gcHandle = GCHandle.Alloc(handler);
        }

        try
        {
            _msiApi.SetInternalUI(InstallUILevelNone, IntPtr.Zero);
            if (handler is not null)
                _msiApi.SetExternalUI(handler, 0x00000400, IntPtr.Zero);

            var commandLine = string.IsNullOrEmpty(additionalArgs) ? null : additionalArgs;
            var exitCode = _msiApi.InstallProduct(msiPath, commandLine);

            if (exitCode != ErrorSuccess && exitCode != ErrorSuccessRebootRequired)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI installation failed with exit code {exitCode}");

            return EncodeExitCode(exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI install failed: {ex.Message}");
        }
        finally
        {
            if (handler is not null)
            {
                _msiApi.SetExternalUI(null, 0, IntPtr.Zero);
                gcHandle.Free();
            }
        }
    }

    private static byte[] EncodeExitCode(uint exitCode)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write(exitCode);
        return stream.ToArray();
    }
}
