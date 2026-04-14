using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
    public static Result<Dictionary<string, byte[]>> Extract(Stream cabinetStream)
    {
        ArgumentNullException.ThrowIfNull(cabinetStream);

        if (!cabinetStream.CanRead)
            return Result<Dictionary<string, byte[]>>.Failure(
                ErrorKind.InvalidOperation, "Cabinet stream must be readable.");

        using var extractor = new CabinetExtractor();
        return extractor.ExtractCore(cabinetStream);
    }

    private Result<Dictionary<string, byte[]>> ExtractFromPathCore(string cabinetPath)
    {
        // RED stub: the real implementation wires fdintNEXT_CABINET for span
        // resolution. This stub falls back to the existing stream-based path
        // which aborts on the first span boundary.
        using var fs = File.OpenRead(cabinetPath);
        return ExtractCore(fs);
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
            using var hfdi = new FdiHandle(NativeMethods.FDICreate(
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

            try
            {
                var success = NativeMethods.FDICopy(
                    hfdi.DangerousGetHandle(),
                    cabName,
                    cabPath,
                    0,
                    _notifyCallback!,
                    nint.Zero,
                    nint.Zero);

                if (!success)
                {
                    var detail = _lastCallbackError is not null
                        ? $" Detail: {_lastCallbackError}"
                        : "";
                    return Result<Dictionary<string, byte[]>>.Failure(
                        ErrorKind.CompilationError,
                        $"FDICopy failed. ERF: oper={erf.erfOper}, type={erf.erfType}.{detail}");
                }
            }
            finally
            {
                CleanupOpenStreams();
            }

            return new Dictionary<string, byte[]>(_extractedFiles);
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

                var ms = new MemoryStream(pfdin.cb > 0 ? pfdin.cb : 4096);
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
                        _extractedFiles[name] = ms.ToArray();
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
                return -1; // Cabinet spanning not supported

            default:
                return nint.Zero;
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