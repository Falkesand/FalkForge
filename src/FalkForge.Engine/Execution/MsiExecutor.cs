namespace FalkForge.Engine.Execution;

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalkForge.Engine.Elevation;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;
using FalkForge.Platform.Windows;

public sealed partial class MsiExecutor
{
    // CA1870: SearchValues is the optimized, cached form of a fixed char set for IndexOfAny.
    private static readonly SearchValues<char> ProhibitedValueChars = SearchValues.Create("\"&|;><");

    private readonly Func<IElevationClient?> _elevationClientAccessor;
    private readonly Func<VariableStore?> _variableStoreAccessor;
    private readonly Func<IMsiApi?> _msiApiAccessor;
    private readonly Func<InstallerManifest?> _manifestAccessor;

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
        : this(elevationClientAccessor, variableStoreAccessor, msiApiAccessor, static () => null)
    {
    }

    /// <summary>
    /// Full constructor. <paramref name="manifestAccessor"/> supplies the installer manifest for the current
    /// session so the elevated MsiInstall payload can carry the publisher-signed integrity envelope the
    /// companion verifies before installing as SYSTEM. Returns null when no manifest is available (a
    /// direct/per-user session or a test), in which case the elevated payload carries an empty manifest and
    /// the companion fails closed.
    /// </summary>
    public MsiExecutor(
        Func<IElevationClient?> elevationClientAccessor,
        Func<VariableStore?> variableStoreAccessor,
        Func<IMsiApi?> msiApiAccessor,
        Func<InstallerManifest?> manifestAccessor)
    {
        _elevationClientAccessor = elevationClientAccessor;
        _variableStoreAccessor = variableStoreAccessor;
        _msiApiAccessor = msiApiAccessor;
        _manifestAccessor = manifestAccessor;
    }

    public async Task<Result<int>> ExecuteAsync(PlanAction action, CancellationToken ct, IProgress<int> packageProgress)
    {
        // Validate custom properties up front (applies to both elevated and direct paths)
        var propsResult = ValidateAndBuildPropertyArgs(action, _variableStoreAccessor());
        if (propsResult.IsFailure)
            return Result<int>.Failure(propsResult.Error);

        // Slipstream patch paths are joined into a PATCH="..." argument string; an embedded
        // quote or newline would break out of the quoting (the elevated MsiInstall parser
        // additionally blocks shell metacharacters in PATCH values — this is the engine-side
        // defense-in-depth gate, applied to both the elevated and direct paths). The full
        // property-value character set is NOT applied here because '&' is legal in real
        // directory names and ';' is the PATCH list separator.
        foreach (var patchPath in action.SlipstreamPatchPaths)
        {
            if (patchPath.AsSpan().IndexOfAny('"', '\r', '\n') >= 0)
                return Result<int>.Failure(
                    ErrorKind.SecurityError,
                    "Slipstream patch path contains prohibited characters");
        }

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

    // Expands a `[VariableName]` property value to a secret variable's plaintext when the variable
    // store holds one under that name. Secret properties collected through SetSecureProperty do NOT
    // travel this path — they never reach the command line at all; they are set through a runtime
    // transform. This resolves only a `[VAR]` reference an author placed in a plain property value.
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

    private async Task<Result<int>> ExecuteElevatedAsync(
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

            // Apply slipstream patches only for install actions
            if (action.ActionType == PlanActionType.Install && action.SlipstreamPatchPaths.Count > 0)
            {
                additionalArgs += $" PATCH=\"{string.Join(';', action.SlipstreamPatchPaths)}\"";
            }

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
                    // EffectiveSourcePath = the extraction-resolved path when the bootstrapper forwarded
                    // a payload root (distributed bundle), else the manifest SourcePath. This is the path
                    // the elevated MsiInstallCommand File.Exists-checks and installs, so it must be the
                    // one that exists on the target machine.
                    writer.Write(action.EffectiveSourcePath);
                    writer.Write(additionalArgs);
                    // The manifest-declared hash for this package. The elevated companion opens the
                    // file itself, hashes it, and compares against this value before installing —
                    // this engine side stays a pure forwarder and does not validate it. Without this
                    // field, a same-user process could overwrite the cached MSI between the engine's
                    // own verification and the elevated install (TOCTOU) and have the swapped bytes
                    // installed as SYSTEM.
                    writer.Write(action.Package.Sha256Hash);

                    // The bundle package id being installed and the full installer manifest (which carries
                    // the publisher-signed integrity envelope). The companion verifies the envelope against
                    // its OWN baked key set and binds the file to the SIGNED hash before installing as
                    // SYSTEM — this engine side stays a pure forwarder and asserts no trust. A null manifest
                    // serializes to an empty string, which the companion refuses (fail closed).
                    writer.Write(action.PackageId);
                    writer.Write(SerializeManifestForCompanion(_manifestAccessor()));

                    // Per-package MSI transforms (D36) travel as a required, length-prefixed block: the
                    // (transformId, resolved extracted path) pairs the ApplyStep resolved under the payload
                    // root. The companion re-binds each to its SIGNED hash and the SIGNED association map
                    // before merging it, so this side stays a pure forwarder. The block is ALWAYS written
                    // (count 0 when the package declares no transform, or for a non-install action) so its
                    // fixed position keeps the optional secret block that follows detectable by stream
                    // position, exactly as before. Only Install carries transforms.
                    var transforms = action.ActionType == PlanActionType.Install
                        ? action.ResolvedTransformPaths
                        : [];
                    writer.Write(transforms.Count);
                    foreach (var (transformId, transformPath) in transforms)
                    {
                        writer.Write(transformId);
                        writer.Write(transformPath);
                    }

                    // Secret properties travel as an optional trailing block: the companion generates a
                    // transform from them in its own SYSTEM-only staging directory and sets them off the
                    // command line. Only Install carries secrets; the block is absent otherwise, keeping
                    // the wire format identical for non-secret installs.
                    if (action.ActionType == PlanActionType.Install && action.SecureProperties.Count > 0)
                    {
                        writer.Write(action.SecureProperties.Count);
                        foreach (var (name, secret) in action.SecureProperties)
                        {
                            writer.Write(name);
                            writer.Write(secret.Length);
                            writer.Write(secret.Span);
                        }
                    }
                }
                payload = stream.ToArray();
            }

            try
            {
                var result = await elevationClient.SendCommandAsync(commandName, payload, ct, packageProgress);
                if (result.IsFailure)
                {
                    return Result<int>.Failure(ErrorKind.ExecutionError, result.Error.Message);
                }

                // Elevated command succeeded — exit code 0
                return 0;
            }
            finally
            {
                // The payload may carry secret property plaintext; zero it once the send completes. The
                // MemoryStream's internal buffer copy cannot be reached to zero here — an acknowledged
                // residual, the same shape as the plaintext byte[] the transport requires.
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<int>.Failure(
                ErrorKind.ExecutionError, $"Elevated MSI execution failed: {ex.Message}");
        }
    }

    private Result<int> ExecuteDirect(PlanAction action, string additionalArgs, IProgress<int> packageProgress)
    {
        // Apply slipstream patches only for install actions
        if (action.ActionType == PlanActionType.Install && action.SlipstreamPatchPaths.Count > 0)
        {
            additionalArgs += $" PATCH=\"{string.Join(';', action.SlipstreamPatchPaths)}\"";
        }

        var msiApi = _msiApiAccessor();
        if (msiApi is null)
            return Result<int>.Failure(ErrorKind.ExecutionError, "MSI API not available");

        // Secret properties (SetSecureProperty) are set through a runtime transform applied with
        // TRANSFORMS=, never on the command line. The per-user (direct) path runs AS the user, who owns
        // the secret they typed, so the transform and its working copy are staged in a fresh, unpredictable
        // per-user temp directory and deleted after the install. Only Install carries a runtime transform.
        string? secretStagingDir = null;
        if (action.SecureProperties.Count > 0
            && action.ActionType == PlanActionType.Install
            && OperatingSystem.IsWindows())
        {
            var stage = StageSecretTransform(action, additionalArgs);
            if (stage.IsFailure)
                return Result<int>.Failure(stage.Error);
            (additionalArgs, secretStagingDir) = stage.Value;
        }

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
                    action.EffectiveSourcePath,
                    string.IsNullOrEmpty(additionalArgs) ? null : additionalArgs.TrimStart()),

                PlanActionType.Uninstall => msiApi.ConfigureProduct(
                    action.Package.Properties.GetValueOrDefault("ProductCode")
                        ?? action.Package.SourcePath,
                    0,  // INSTALLLEVEL_DEFAULT
                    2), // INSTALLSTATE_ABSENT

                PlanActionType.Repair => msiApi.InstallProduct(
                    action.EffectiveSourcePath,
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
            // Delete the staged transform and its working copy. Both held the secret in plaintext for the
            // install's duration; neither survives this call.
            DeleteStagingDirectory(secretStagingDir);
        }
    }

    /// <summary>
    /// Generates a secret-property transform for <paramref name="action"/> in a fresh per-user temp
    /// directory and merges it into <paramref name="additionalArgs"/>. Returns the updated arguments and
    /// the staging directory the caller must delete after the install.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Result<(string additionalArgs, string stagingDir)> StageSecretTransform(
        PlanAction action, string additionalArgs)
    {
        string stagingDir;
        try
        {
            stagingDir = Directory.CreateTempSubdirectory("ff-mst-").FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<(string, string)>.Failure(ErrorKind.IoError,
                $"Failed to create a staging directory for the secret transform: {ex.Message}");
        }

        var gen = MsiTransformGenerator.GenerateSecretTransform(
            action.EffectiveSourcePath, action.SecureProperties, stagingDir);
        if (gen.IsFailure)
        {
            DeleteStagingDirectory(stagingDir);
            return Result<(string, string)>.Failure(gen.Error);
        }

        var merged = MsiTransformArgs.MergeTransforms(additionalArgs, gen.Value);
        return (merged, stagingDir);
    }

    private static void DeleteStagingDirectory(string? stagingDir)
    {
        if (stagingDir is null)
            return;

        try
        {
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a failure to delete the staging directory must never mask the
            // install result. A crash-swept sibling directory handles the elevated path; the per-user
            // temp directory is reclaimed by the OS.
        }
    }

    /// <summary>
    /// Serializes the session manifest into the JSON the elevated companion deserializes and hands to the
    /// integrity gate. Uses the shared Protocol source-generated context (AOT-safe, and the SAME context the
    /// companion parses with, so the two never drift). A null manifest yields an empty string, which the
    /// companion treats as "no proof of authorship" and refuses.
    /// </summary>
    private static string SerializeManifestForCompanion(InstallerManifest? manifest)
        => manifest is null
            ? string.Empty
            : JsonSerializer.Serialize(manifest, BundleTrustJsonContext.Default.InstallerManifest);

    // \A/\z rather than ^/$: in .NET, $ matches end-of-string OR immediately before a single
    // trailing '\n' even without RegexOptions.Multiline, so an otherwise-legal key with a
    // trailing newline would slip through an otherwise-correct ^...$ anchor -- and prop.Key is
    // appended UNQUOTED directly into the msiexec.exe argument string below.
    [GeneratedRegex(@"\A[A-Z_][A-Z0-9_.]*\z")]
    private static partial Regex MsiPropertyKeyPattern();
}
