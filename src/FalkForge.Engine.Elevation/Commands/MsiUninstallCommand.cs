namespace FalkForge.Engine.Elevation.Commands;

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FalkForge.Platform.Windows;

public sealed partial class MsiUninstallCommand : IElevatedCommand
{
    private const int InstallUILevelNone = 2;
    private const int InstallLevelDefault = 0;
    private const int InstallStateAbsent = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorSuccessRebootRequired = 3010;

    private readonly IMsiApi _msiApi;

    public MsiUninstallCommand(IMsiApi msiApi)
    {
        _msiApi = msiApi;
    }

    public string Name => "MsiUninstall";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        var productCode = reader.ReadString();

        if (!GuidPattern().IsMatch(productCode))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Product code must be a valid GUID in the format {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}");

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

            var exitCode = _msiApi.ConfigureProduct(productCode, InstallLevelDefault, InstallStateAbsent);

            if (exitCode != ErrorSuccess && exitCode != ErrorSuccessRebootRequired)
                return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI uninstall failed with exit code {exitCode}");

            return EncodeExitCode(exitCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<byte[]>.Failure(ErrorKind.ExecutionError, $"MSI uninstall failed: {ex.Message}");
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

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex GuidPattern();
}
