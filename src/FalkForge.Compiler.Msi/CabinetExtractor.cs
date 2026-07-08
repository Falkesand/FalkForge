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
public sealed class CabinetExtractor : IDisposable
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
        _allocCallback = CbAlloc;
        _freeCallback = CbFree;
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

    // ── Callback implementations ────────────────────────────────────────

    private static nint CbAlloc(uint cb)
    {
        return Marshal.AllocHGlobal((int)cb);
    }

    private static void CbFree(nint memory)
    {
        Marshal.FreeHGlobal(memory);
    }

    private nint CbOpen(string pszFile, int oflag, int pmode)
    {
        try
        {
            var (mode, access) = MapOpenFlags(oflag);
            var stream = new FileStream(pszFile, mode, access, FileShare.Read);
            var handle = (nint)_nextInputHandle++;
            _openStreams[handle] = stream;
            return handle;
        }
        catch (Exception ex)
        {
            _lastCallbackError = $"Open failed for '{pszFile}': {ex.Message}";
            return -1;
        }
    }

    private uint CbRead(nint hf, nint pv, uint cb)
    {
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                _lastCallbackError = $"Read: handle {hf} not found";
                return unchecked((uint)-1);
            }

            var buffer = ArrayPool<byte>.Shared.Rent((int)cb);
            try
            {
                var bytesRead = stream.Read(buffer, 0, (int)cb);
                Marshal.Copy(buffer, 0, pv, bytesRead);
                return (uint)bytesRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _lastCallbackError = $"Read failed: {ex.Message}";
            return unchecked((uint)-1);
        }
    }

    private uint CbWrite(nint hf, nint pv, uint cb)
    {
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                _lastCallbackError = $"Write: handle {hf} not found";
                return unchecked((uint)-1);
            }

            var buffer = ArrayPool<byte>.Shared.Rent((int)cb);
            try
            {
                Marshal.Copy(pv, buffer, 0, (int)cb);
                stream.Write(buffer, 0, (int)cb);
                return cb;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex)
        {
            _lastCallbackError = $"Write failed: {ex.Message}";
            return unchecked((uint)-1);
        }
    }

    private int CbClose(nint hf)
    {
        try
        {
            if (_openStreams.Remove(hf, out var stream)) stream.Dispose();
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    private int CbSeek(nint hf, int dist, int seektype)
    {
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                _lastCallbackError = $"Seek: handle {hf} not found";
                return -1;
            }

            var origin = seektype switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin
            };

            var newPos = stream.Seek(dist, origin);
            return (int)newPos;
        }
        catch (Exception ex)
        {
            _lastCallbackError = $"Seek failed: {ex.Message}";
            return -1;
        }
    }

    private nint CbNotify(int fdint, nint pfdinPtr)
    {
        // pfdin.cb (uncompressed size) is read straight from the CFFILE record of a
        // possibly-attacker-controlled cabinet — completely unvalidated. A crafted/corrupt
        // cab can set it to int.MaxValue, and every other callback (CbOpen/CbRead/CbWrite/
        // CbSeek/CbClose) already wraps its body in try/catch so a native P/Invoke boundary
        // never sees a managed exception; CbNotify must do the same so a bad allocation here
        // surfaces as a Result<T>.Failure instead of an unhandled OutOfMemoryException.
        try
        {
            var pfdin = Marshal.PtrToStructure<NativeMethods.FdiNotification>(pfdinPtr);

            switch (fdint)
            {
                case NativeMethods.FdintCopyFile:
                {
                    // A file is about to be extracted. Return a handle for the output stream,
                    // or 0 to skip, or -1 to abort.
                    var fileName = Marshal.PtrToStringAnsi(pfdin.psz1);
                    if (fileName is null)
                        return nint.Zero; // Skip

                    // pfdin.cb is only a capacity HINT — the stream grows automatically as
                    // real bytes arrive via CbWrite — so a wrong/hostile hint must never
                    // drive the initial allocation. Enforce the decompression-bomb budget
                    // against the DECLARED size here, before any bytes are buffered (the
                    // FdintCloseFileInfo check further below only sees the ACTUAL bytes,
                    // which is too late once a huge buffer has already been requested).
                    var declaredSize = pfdin.cb;
                    if (declaredSize > 0)
                    {
                        var remainingBudget = _maxTotalBytes == long.MaxValue
                            ? long.MaxValue
                            : Math.Max(0, _maxTotalBytes - _totalExtractedBytes);

                        if (declaredSize > remainingBudget)
                        {
                            _budgetExceeded = true;
                            _lastCallbackError =
                                $"Declared uncompressed size {declaredSize} for '{fileName}' exceeds the remaining {remainingBudget}-byte budget.";
                            return -1; // Abort FDICopy before allocating.
                        }
                    }

                    // Clamp the capacity hint to a modest ceiling instead of trusting it
                    // outright: legitimate files still get a reasonable pre-size (avoiding
                    // MemoryStream's default grow-by-doubling churn), while a malformed
                    // int.MaxValue hint can no longer force `new MemoryStream(int.MaxValue)`
                    // to throw (exceeds the CLR's ~0x7FFFFFC7 array-length ceiling).
                    const int initialCapacityCeiling = 1024 * 1024; // 1 MiB
                    var initialCapacity = declaredSize > 0
                        ? Math.Min(declaredSize, initialCapacityCeiling)
                        : 4096;

                    var ms = new MemoryStream(initialCapacity);
                    var handle = (nint)_nextOutputHandle++;
                    _openStreams[handle] = ms;
                    _outputHandleNames[handle] = fileName;
                    return handle;
                }

                case NativeMethods.FdintCloseFileInfo:
                {
                    // A file has been fully extracted. Collect the bytes and close the stream.
                    var handle = pfdin.hf;
                    if (_openStreams.TryGetValue(handle, out var stream) && stream is MemoryStream ms)
                    {
                        if (_outputHandleNames.TryGetValue(handle, out var name))
                        {
                            var bytes = ms.ToArray();

                            // Decompression-bomb guard: abort once the cumulative uncompressed size
                            // would exceed the configured budget, before retaining these bytes.
                            _totalExtractedBytes += bytes.LongLength;
                            if (_totalExtractedBytes > _maxTotalBytes)
                            {
                                _budgetExceeded = true;
                                _lastCallbackError =
                                    $"Cumulative uncompressed size exceeded the {_maxTotalBytes}-byte budget.";
                                ms.Dispose();
                                _openStreams.Remove(handle);
                                _outputHandleNames.Remove(handle);
                                return -1; // Abort FDICopy.
                            }

                            _extractedFiles[name] = bytes;
                            _outputHandleNames.Remove(handle);
                        }

                        ms.Dispose();
                        _openStreams.Remove(handle);
                    }

                    // Return TRUE (non-zero) to indicate success.
                    return 1;
                }

                case NativeMethods.FdintCabinetInfo:
                case NativeMethods.FdintPartialFile:
                case NativeMethods.FdintEnumerate:
                    return nint.Zero; // Continue

                case NativeMethods.FdintNextCabinet:
                {
                    // FDI asks where to find the next cabinet in the span. Streams
                    // mode (no _cabinetDirectory) cannot span — abort explicitly.
                    if (_cabinetDirectory is null)
                    {
                        _lastCallbackError = "Spanned cabinets require ExtractFromPath; the stream-based Extract overload cannot resolve continuations.";
                        return -1;
                    }

                    var continuationName = Marshal.PtrToStringAnsi(pfdin.psz1);
                    var resolveResult = _chainResolver.Resolve(_cabinetDirectory, continuationName);
                    if (resolveResult.IsFailure)
                    {
                        _lastCallbackError = resolveResult.Error.Message;
                        return -1;
                    }

                    // Overwrite the psz3 buffer with the resolved cab's directory
                    // (ANSI). FDI supplies a 256-char writable buffer for the
                    // callback to redirect. Path + trailing backslash + NUL must
                    // fit; the spec limit is MAX_PATH (260 bytes) for cab path
                    // fields. Refuse rather than truncate so we never hand FDI a
                    // corrupt path.
                    var resolvedDir = Path.GetDirectoryName(resolveResult.Value) ?? _cabinetDirectory;
                    var dirWithSep = EnsureTrailingBackslash(resolvedDir);
                    var bytes = System.Text.Encoding.Default.GetBytes(dirWithSep);
                    const int maxCabPathBytes = 255;
                    if (bytes.Length > maxCabPathBytes)
                    {
                        _lastCallbackError = $"Cabinet directory path '{resolvedDir}' exceeds FDI's {maxCabPathBytes}-byte cab-path limit.";
                        return -1;
                    }

                    Marshal.Copy(bytes, 0, pfdin.psz3, bytes.Length);
                    Marshal.WriteByte(pfdin.psz3, bytes.Length, 0);
                    return nint.Zero; // Continue with the new path
                }

                default:
                    return nint.Zero;
            }
        }
        catch (Exception ex)
        {
            // Matches the try/catch pattern in every sibling callback (CbOpen/CbRead/
            // CbWrite/CbSeek/CbClose): never let a managed exception cross the native
            // FDICopy P/Invoke boundary. Record the error and abort; ExtractCore/
            // ExtractFromPathCore turn this into a Result<T>.Failure.
            _lastCallbackError = $"Notify failed (fdint={fdint}): {ex.Message}";
            return -1;
        }
    }

    // ── C-style open flag mapping ───────────────────────────────────────

    private static (FileMode mode, FileAccess access) MapOpenFlags(int oflag)
    {
        const int oRdonly = 0x0000;
        const int oWronly = 0x0001;
        const int oRdwr = 0x0002;
        const int oCreat = 0x0100;
        const int oTrunc = 0x0200;

        var accessMode = oflag & 0x0003;
        var access = accessMode switch
        {
            oRdonly => FileAccess.Read,
            oWronly => FileAccess.Write,
            oRdwr => FileAccess.ReadWrite,
            _ => FileAccess.ReadWrite
        };

        FileMode mode;
        if ((oflag & oCreat) != 0 && (oflag & oTrunc) != 0)
            mode = FileMode.Create;
        else if ((oflag & oCreat) != 0)
            mode = FileMode.OpenOrCreate;
        else if ((oflag & oTrunc) != 0)
            mode = FileMode.Truncate;
        else
            mode = FileMode.Open;

        return (mode, access);
    }
}