using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

/// <summary>
///     Extracts files from a Windows cabinet (.cab) archive using the FDI (File Decompression Interface) API.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class CabinetExtractor : IDisposable
{
    // Extracted file data collected during FDICopy via notifications.
    private readonly Dictionary<string, byte[]> _extractedFiles = new();

    // File handle tracking: maps pseudo-handles to Stream instances.
    // Input handles start from 1, output handles start from 10000 to avoid collisions.
    // FDI reserves 0 (skip) and -1 (abort) as special return values in notifications.
    private readonly Dictionary<nint, Stream> _openStreams = new();

    // Maps output handles to their file names for collection in CloseFileInfo.
    private readonly Dictionary<nint, string> _outputHandleNames = new();

    // Pinned callback delegates - prevent GC collection during P/Invoke
    private NativeMethods.FnFciAlloc? _allocCallback;
    private NativeMethods.FnFdiClose? _closeCallback;
    private NativeMethods.FnFciFree? _freeCallback;

    // Tracks the last callback error for diagnostic messages on failure.
    private string? _lastCallbackError;

    // Decompression-bomb guard. Maximum cumulative uncompressed bytes this extractor will
    // collect before aborting; the running total is summed as each file completes. Default is
    // unbounded (long.MaxValue) so existing callers — notably "forge extract" — are unchanged.
    private long _maxTotalBytes = long.MaxValue;
    private long _totalExtractedBytes;
    private bool _budgetExceeded;

    // Directory that contains the primary cabinet on disk. fdintNEXT_CABINET
    // resolves continuation names against this directory so span chains work.
    // Null when extracting from a stream (spanning is unsupported in that mode).
    private string? _cabinetDirectory;

    // Span-resolution policy. Default is the filesystem resolver; tests can
    // swap it for a stub that injects unsafe names or missing files without
    // needing to drive real Windows FDI.
    private readonly ICabinetChainResolver _chainResolver = new FileSystemCabinetChainResolver();
    private int _nextInputHandle = 1;
    private int _nextOutputHandle = 10_000;
    private NativeMethods.FnFdiNotify? _notifyCallback;
    private NativeMethods.FnFdiOpen? _openCallback;
    private NativeMethods.FnFdiRead? _readCallback;
    private NativeMethods.FnFdiSeek? _seekCallback;
    private NativeMethods.FnFdiWrite? _writeCallback;

    public void Dispose()
    {
        CleanupOpenStreams();
    }

    /// <summary>
    ///     Extracts all files from a cabinet located on disk, following the
    ///     <c>fdintNEXT_CABINET</c> chain for spanned cabs. Pass the path of
    ///     the first cab in the chain; continuation cabs must sit next to it.
    /// </summary>
    public static Result<Dictionary<string, byte[]>> ExtractFromPath(string cabinetPath)
    {
        ArgumentNullException.ThrowIfNull(cabinetPath);
        if (!File.Exists(cabinetPath))
            return Result<Dictionary<string, byte[]>>.Failure(
                ErrorKind.InvalidOperation, $"Cabinet file '{cabinetPath}' does not exist.");

        using var extractor = new CabinetExtractor();
        return extractor.ExtractFromPathCore(cabinetPath);
    }

    /// <summary>
    ///     Validates a continuation cabinet name supplied by FDI against the
    ///     <c>fdintNEXT_CABINET</c> callback. Continuations must be plain file
    ///     names (no path separators, no <c>..</c>, not absolute). Exposed
    ///     for tests; the extractor itself uses it internally.
    /// </summary>
    public static bool IsSafeContinuationName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (Path.IsPathRooted(name)) return false;
        // GetFileName strips any separators; if the result differs the input
        // carried something path-like even when the guards above miss it.
        return string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Extracts all files from a cabinet stream into memory. Cabinet spanning
    ///     is not supported through this entry point — use
    ///     <see cref="ExtractFromPath" /> for multi-disk cabs.
    /// </summary>
    /// <param name="cabinetStream">The cabinet data stream. Must be readable.</param>
    /// <returns>A dictionary mapping file names to their extracted byte contents.</returns>
    public static Result<Dictionary<string, byte[]>> Extract(Stream cabinetStream) =>
        Extract(cabinetStream, long.MaxValue);

    /// <summary>
    ///     Extracts all files from a cabinet stream into memory, aborting if the cumulative
    ///     uncompressed size exceeds <paramref name="maxTotalBytes"/>. This bounds the memory
    ///     a hostile (zip-bomb) cabinet can force the process to allocate.
    /// </summary>
    /// <param name="cabinetStream">The cabinet data stream. Must be readable.</param>
    /// <param name="maxTotalBytes">
    ///     Maximum cumulative uncompressed bytes to collect. Use <see cref="long.MaxValue"/>
    ///     for unbounded extraction (the historical behaviour).
    /// </param>
    /// <returns>A dictionary mapping file names to their extracted byte contents.</returns>
    public static Result<Dictionary<string, byte[]>> Extract(Stream cabinetStream, long maxTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(cabinetStream);

        if (!cabinetStream.CanRead)
            return Result<Dictionary<string, byte[]>>.Failure(
                ErrorKind.InvalidOperation, "Cabinet stream must be readable.");

        using var extractor = new CabinetExtractor { _maxTotalBytes = maxTotalBytes };
        return extractor.ExtractCore(cabinetStream);
    }

    private Result<Dictionary<string, byte[]>> ExtractFromPathCore(string cabinetPath)
    {
        var fullPath = Path.GetFullPath(cabinetPath);
        var cabName = Path.GetFileName(fullPath);
        var cabDir = Path.GetDirectoryName(fullPath)
                     ?? throw new InvalidOperationException($"Cabinet path '{fullPath}' has no directory component.");

        // Remember the directory so fdintNEXT_CABINET can resolve continuations
        // against it. Every continuation name is validated through
        // IsSafeContinuationName before being joined to this directory.
        _cabinetDirectory = cabDir;

        InitializeCallbacks();

        var erf = new NativeMethods.ERF();
        var hfdi = new FdiHandle(NativeMethods.FDICreate(
            _allocCallback!,
            _freeCallback!,
            _openCallback!,
            _readCallback!,
            _writeCallback!,
            _closeCallback!,
            _seekCallback!,
            -1, // cpuAUTO
            ref erf));

        if (hfdi.IsInvalid)
            return Result<Dictionary<string, byte[]>>.Failure(
                ErrorKind.CompilationError,
                $"FDICreate failed. ERF: oper={erf.erfOper}, type={erf.erfType}");

        var cabPathWithSep = EnsureTrailingBackslash(cabDir);
        return RunFdiCopy(hfdi, cabName, cabPathWithSep, in erf);
    }

    private Result<Dictionary<string, byte[]>> ExtractCore(Stream cabinetStream)
    {
        // FDI requires file-based I/O callbacks, so copy the stream to a temp file.
        string? tempFile = null;
        try
        {
            tempFile = Path.Combine(Path.GetTempPath(), $"fdi_{Guid.NewGuid():N}.cab");
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                cabinetStream.CopyTo(fs);
            }

            // Split path: FDICopy requires cabinet name and directory separately.
            var cabName = Path.GetFileName(tempFile);
            var cabPath = EnsureTrailingBackslash(Path.GetDirectoryName(tempFile)!);

            InitializeCallbacks();

            var erf = new NativeMethods.ERF();
            var hfdi = new FdiHandle(NativeMethods.FDICreate(
                _allocCallback!,
                _freeCallback!,
                _openCallback!,
                _readCallback!,
                _writeCallback!,
                _closeCallback!,
                _seekCallback!,
                -1, // cpuAUTO
                ref erf));

            if (hfdi.IsInvalid)
                return Result<Dictionary<string, byte[]>>.Failure(
                    ErrorKind.CompilationError,
                    $"FDICreate failed. ERF: oper={erf.erfOper}, type={erf.erfType}");

            return RunFdiCopy(hfdi, cabName, cabPath, in erf);
        }
        finally
        {
            if (tempFile is not null && File.Exists(tempFile))
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    /* Best-effort cleanup */
                }
        }
    }

    /// <summary>
    ///     Runs FDICopy against an already-created FDI handle and turns the result into a
    ///     <see cref="Result{T}" />, disposing the handle and open streams no matter the
    ///     outcome. Shared by <see cref="ExtractFromPathCore" /> and <see cref="ExtractCore" />
    ///     — the only difference between the two call sites is how <paramref name="cabName" />
    ///     and <paramref name="cabDirectory" /> were derived (disk path vs. temp-file copy).
    /// </summary>
    private Result<Dictionary<string, byte[]>> RunFdiCopy(
        FdiHandle hfdi, string cabName, string cabDirectory, in NativeMethods.ERF erf)
    {
        Result<Dictionary<string, byte[]>>? failure = null;
        try
        {
            var success = NativeMethods.FDICopy(
                hfdi.DangerousGetHandle(),
                cabName,
                cabDirectory,
                0,
                _notifyCallback!,
                nint.Zero,
                nint.Zero);

            if (!success)
            {
                failure = _budgetExceeded
                    ? Result<Dictionary<string, byte[]>>.Failure(
                        ErrorKind.LayoutError,
                        $"Cabinet extraction aborted: {_lastCallbackError}")
                    : Result<Dictionary<string, byte[]>>.Failure(
                        ErrorKind.CompilationError,
                        $"FDICopy failed. ERF: oper={erf.erfOper}, type={erf.erfType}." +
                        (_lastCallbackError is not null ? $" Detail: {_lastCallbackError}" : ""));
            }
        }
        finally
        {
            CleanupOpenStreams();

            // FDIDestroy (invoked by hfdi.Dispose) fires native callbacks — at minimum
            // the free callback for internal FDI buffers. GC.KeepAlive must come AFTER
            // Dispose so that 'this' (and every rooted delegate field) remains reachable
            // through the entire destroy sequence. A KeepAlive placed before Dispose
            // leaves the dispose window uncovered and the delegates can be collected mid-
            // callback, crashing with "A callback was made on a garbage collected delegate".
            hfdi.Dispose();
            GC.KeepAlive(this);
        }

        if (failure is not null)
            return failure.Value;

        return new Dictionary<string, byte[]>(_extractedFiles);
    }

    private static string EnsureTrailingBackslash(string path)
    {
        if (path.Length > 0 && path[^1] != '\\' && path[^1] != '/')
            return path + "\\";
        return path;
    }

    private void InitializeCallbacks()
    {
        _allocCallback = CabinetCallbackShim.Alloc;
        _freeCallback = CabinetCallbackShim.Free;
        _openCallback = CbOpen;
        _readCallback = CbRead;
        _writeCallback = CbWrite;
        _closeCallback = CbClose;
        _seekCallback = CbSeek;
        _notifyCallback = CbNotify;
    }

    private void CleanupOpenStreams()
    {
        foreach (var stream in _openStreams.Values) stream.Dispose();
        _openStreams.Clear();
        _outputHandleNames.Clear();
        _cabinetDirectory = null;
    }
}