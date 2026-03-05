namespace FalkForge.Engine.Execution;

using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Variables;
using FalkForge.Platform.Windows;

public sealed partial class MsiExecutor
{
    private static readonly char[] ProhibitedValueChars = ['"', '&', '|', ';', '>', '<'];

    private readonly Func<IElevationClient?> _elevationClientAccessor;
    private readonly Func<VariableStore?> _variableStoreAccessor;
    private readonly Func<IMsiApi?> _msiApiAccessor;

    public MsiExecutor()
        : this(static () => null, static () => null, static () => null)
    {
    }

    public MsiExecutor(Func<IElevationClient?> elevationClientAccessor)
        : this(elevationClientAccessor, static () => null, static () => null)
    {
    }

    public MsiExecutor(Func<IElevationClient?> elevationClientAccessor, Func<VariableStore?> variableStoreAccessor)
        : this(elevationClientAccessor, variableStoreAccessor, static () => null)
    {
    }

    public MsiExecutor(
        Func<IElevationClient?> elevationClientAccessor,
        Func<VariableStore?> variableStoreAccessor,
        Func<IMsiApi?> msiApiAccessor)
    {
        _elevationClientAccessor = elevationClientAccessor;
        _variableStoreAccessor = variableStoreAccessor;
        _msiApiAccessor = msiApiAccessor;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        // Validate custom properties up front (applies to both elevated and direct paths)
        var propsResult = ValidateAndBuildPropertyArgs(action, _variableStoreAccessor());
        if (propsResult.IsFailure)
            return Result<int>.Failure(propsResult.Error);

        var elevationClient = _elevationClientAccessor();
        if (elevationClient is not null)
        {
            return await ExecuteElevatedAsync(action, propsResult.Value, elevationClient, ct, packageProgress);
        }

        return ExecuteDirect(action, propsResult.Value, packageProgress);
    }

    private static Result<string> ValidateAndBuildPropertyArgs(PlanAction action, VariableStore? variableStore)
    {
        if (action.Properties.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (var prop in action.Properties)
        {
            if (!MsiPropertyKeyPattern().IsMatch(prop.Key))
                return Result<string>.Failure(
                    ErrorKind.SecurityError,
                    $"Invalid MSI property key '{prop.Key}': must match ^[A-Z_][A-Z0-9_.]*$");

            var resolvedValue = ResolvePropertyValue(prop.Value, variableStore);

            if (resolvedValue.AsSpan().IndexOfAny(ProhibitedValueChars) >= 0)
                return Result<string>.Failure(
                    ErrorKind.SecurityError,
                    $"MSI property value for '{prop.Key}' contains prohibited characters");

            sb.Append(' ');
            sb.Append(prop.Key);
            sb.Append("=\"");
            sb.Append(resolvedValue);
            sb.Append('"');
        }

        return sb.ToString();
    }

    private static string ResolvePropertyValue(string value, VariableStore? variableStore)
    {
        if (variableStore is null || value.Length < 3)
            return value;

        // Resolve [VariableName] references to secret variable values
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var variableName = value[1..^1];
            if (variableStore.IsSecret(variableName))
            {
                var secretResult = variableStore.GetSecret(variableName);
                if (secretResult.IsSuccess)
                    return secretResult.Value;
            }
        }

        return value;
    }

    private static async Task<Result<int>> ExecuteElevatedAsync(
        PlanAction action,
        string additionalArgs,
        IElevationClient elevationClient,
        CancellationToken ct,
        IProgress<int> packageProgress)
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

            var result = await elevationClient.SendCommandAsync(commandName, payload, ct, packageProgress);
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

    private Result<int> ExecuteDirect(PlanAction action, string additionalArgs, IProgress<int> packageProgress)
    {
        var msiApi = _msiApiAccessor();
        if (msiApi is null)
            return Result<int>.Failure(ErrorKind.ExecutionError, "MSI API not available");

        var progressState = new MsiProgressState();
        MsiExternalUIHandler handler = (context, messageType, message) =>
        {
            var percent = progressState.ProcessMessage(messageType, message);
            if (percent >= 0)
                packageProgress.Report(percent);
            return 0; // IDOK
        };

        var gcHandle = GCHandle.Alloc(handler);
        try
        {
            msiApi.SetInternalUI(2, IntPtr.Zero); // INSTALLUILEVEL_NONE
            msiApi.SetExternalUI(handler, 0x00000400, IntPtr.Zero); // INSTALLLOGMODE_PROGRESS

            uint exitCode = action.ActionType switch
            {
                PlanActionType.Install => msiApi.InstallProduct(
                    action.Package.SourcePath,
                    string.IsNullOrEmpty(additionalArgs) ? null : additionalArgs.TrimStart()),

                PlanActionType.Uninstall => msiApi.ConfigureProduct(
                    action.Package.Properties.GetValueOrDefault("ProductCode")
                        ?? action.Package.SourcePath,
                    0,  // INSTALLLEVEL_DEFAULT
                    2), // INSTALLSTATE_ABSENT

                PlanActionType.Repair => msiApi.InstallProduct(
                    action.Package.SourcePath,
                    string.IsNullOrEmpty(additionalArgs)
                        ? "REINSTALL=ALL REINSTALLMODE=vomus"
                        : $"REINSTALL=ALL REINSTALLMODE=vomus{additionalArgs}"),

                _ => throw new InvalidOperationException($"Unknown action type: {action.ActionType}")
            };

            return (int)exitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"MSI execution failed: {ex.Message}");
        }
        finally
        {
            msiApi.SetExternalUI(null, 0, IntPtr.Zero);
            gcHandle.Free();
        }
    }

    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$")]
    private static partial Regex MsiPropertyKeyPattern();
}
