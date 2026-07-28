namespace FalkForge.Engine.Elevation.Commands;

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
        }

        // No GCHandle needed to root `handler`: it is read again in the finally block below, so
        // the JIT keeps it live as a local for this call's entire synchronous extent -- and while
        // registered, WindowsMsiApi.SetExternalUI's own wrapper closes over `handler`, so its
        // static root (see WindowsMsiApi._rootedHandler) transitively keeps it alive too. A prior
        // version of this method pinned `handler` itself via GCHandle, which rooted the wrong
        // object relative to the actual bug: the delegate WindowsMsiApi hands to msi.dll is the
        // wrapper lambda it builds internally around `handler`, not `handler` itself.
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
                _msiApi.SetExternalUI(null, 0, IntPtr.Zero);
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
