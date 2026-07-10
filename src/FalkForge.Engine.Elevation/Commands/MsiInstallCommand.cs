namespace FalkForge.Engine.Elevation.Commands;

using System.Buffers;
using System.Runtime.InteropServices;
using FalkForge.Platform.Windows;

public sealed class MsiInstallCommand : IElevatedCommand
{
    private const int InstallUILevelNone = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorSuccessRebootRequired = 3010;
    // The engine (MsiExecutor.ValidateAndBuildPropertyArgs) assembles additionalArgs as a
    // sequence of ` NAME="VALUE"` pairs: every value is wrapped in double-quotes, pairs are
    // separated by whitespace, and slipstream patches arrive as ` PATCH="a;b"` (paths joined
    // with ';'). Defense-in-depth here therefore PARSES that structure and re-applies the
    // engine's per-VALUE rules, instead of scanning the whole string: a whole-string
    // blocklist containing '"' would reject every legitimate property-bearing install, while
    // one without it would let a value smuggle an extra property. The double-quote itself can
    // never occur inside a parsed value (the first '"' terminates it), so an embedded-quote
    // injection attempt surfaces as a structural (malformed) failure. This flows into
    // MsiInstallProduct (a P/Invoke, NOT a shell), so whitespace between pairs is legitimate.
    // Set mirrors the engine-side MsiExecutor.ProhibitedValueChars minus the structural quote.
    // CA1870: SearchValues is the optimized, cached form of a fixed char set for IndexOfAny.
    private static readonly SearchValues<char> ProhibitedValueChars =
        SearchValues.Create("&|;><");

    // PATCH is the one property whose value legitimately contains ';' — the engine joins
    // multiple slipstream patch paths with it (MsiExecutor.ExecuteElevatedAsync).
    private static readonly SearchValues<char> ProhibitedPatchValueChars =
        SearchValues.Create("&|><");

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

        var argsValidation = ValidateAdditionalArgs(additionalArgs);
        if (argsValidation.IsFailure)
            return Result<byte[]>.Failure(argsValidation.Error);

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

    /// <summary>
    /// Validates that <paramref name="additionalArgs"/> matches the exact wire format the
    /// engine produces — zero or more space-separated <c>NAME="VALUE"</c> pairs, keys matching
    /// the engine's <c>^[A-Z_][A-Z0-9_.]*$</c> rule — and that each unwrapped VALUE is free of
    /// the engine-prohibited characters. A forged or misused peer must not be able to inject
    /// an extra MSI property via an embedded quote in a value; any structural deviation
    /// (unbalanced quotes, missing separators, unquoted values) is rejected as a security
    /// failure.
    /// </summary>
    private static Result<Unit> ValidateAdditionalArgs(string additionalArgs)
    {
        var span = additionalArgs.AsSpan();
        var i = 0;
        while (i < span.Length)
        {
            // Every pair is prefixed by at least one separating space.
            if (span[i] != ' ')
                return MalformedArgs();
            while (i < span.Length && span[i] == ' ')
                i++;
            if (i == span.Length)
                break;

            // NAME: first char [A-Z_], rest [A-Z0-9_.] — mirrors MsiExecutor.MsiPropertyKeyPattern.
            var keyStart = i;
            if (!IsKeyStartChar(span[i]))
                return MalformedArgs();
            i++;
            while (i < span.Length && IsKeyChar(span[i]))
                i++;
            var key = span[keyStart..i];

            // '=' followed by the opening quote.
            if (i + 1 >= span.Length || span[i] != '=' || span[i + 1] != '"')
                return MalformedArgs();
            i += 2;

            // VALUE runs to the next quote; a missing closing quote means unbalanced input.
            var closeOffset = span[i..].IndexOf('"');
            if (closeOffset < 0)
                return MalformedArgs();
            var value = span.Slice(i, closeOffset);
            i += closeOffset + 1;
            // The next loop iteration requires a space here, so a closing quote followed by
            // anything else (an embedded-quote smuggle like PROP="a"EVIL="x") is malformed.

            var prohibited = key.SequenceEqual("PATCH") ? ProhibitedPatchValueChars : ProhibitedValueChars;
            if (value.IndexOfAny(prohibited) >= 0)
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"MSI property value for '{key}' contains prohibited characters");
        }

        return Unit.Value;

        static Result<Unit> MalformedArgs() => Result<Unit>.Failure(ErrorKind.SecurityError,
            "Additional arguments are malformed: expected space-separated NAME=\"VALUE\" property pairs");
    }

    private static bool IsKeyStartChar(char c) => c is (>= 'A' and <= 'Z') or '_';

    private static bool IsKeyChar(char c) => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.';

    private static byte[] EncodeExitCode(uint exitCode)
    {
        using var stream = new MemoryStream(4);
        using var writer = new BinaryWriter(stream);
        writer.Write(exitCode);
        return stream.ToArray();
    }
}
