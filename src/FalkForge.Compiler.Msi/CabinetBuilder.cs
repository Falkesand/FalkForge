using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class CabinetBuilder : IDisposable
{
    private const string CabinetFileName = "Data.cab";

    // File handle tracking: maps pseudo-handles to FileStream instances.
    // FCI callbacks use these to perform file I/O through managed streams.
    private readonly Dictionary<nint, FileStream> _openStreams = new();
    private int _nextHandle = 1;

    // Pinned callback delegates - must survive until FCIDestroy completes
    private NativeMethods.FnFciAlloc? _allocCallback;
    private NativeMethods.FnFciFree? _freeCallback;
    private NativeMethods.FnFciOpen? _openCallback;
    private NativeMethods.FnFciRead? _readCallback;
    private NativeMethods.FnFciWrite? _writeCallback;
    private NativeMethods.FnFciClose? _closeCallback;
    private NativeMethods.FnFciSeek? _seekCallback;
    private NativeMethods.FnFciDelete? _deleteCallback;
    private NativeMethods.FnFciFilePlaced? _filePlacedCallback;
    private NativeMethods.FnFciGetTempFile? _getTempFileCallback;
    private NativeMethods.FnFciGetNextCabinet? _getNextCabinetCallback;
    private NativeMethods.FnFciStatus? _statusCallback;
    private NativeMethods.FnFciGetOpenInfo? _getOpenInfoCallback;

    public Result<string> BuildCabinet(
        IReadOnlyList<ResolvedFile> files,
        string outputPath,
        CompressionLevel compression)
    {
        if (files.Count == 0)
            return Result<string>.Failure(ErrorKind.InvalidOperation, "Cannot build a cabinet with no files.");

        var tcomp = MapCompressionLevel(compression);
        var cabPath = EnsureTrailingBackslash(outputPath);

        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        var ccab = new NativeMethods.CCAB
        {
            cb = 0x7FFFFFFF, // ~2GB max cabinet size
            cbFolderThresh = 0x7FFFFFFF,
            cbReserveCFHeader = 0,
            cbReserveCFFolder = 0,
            cbReserveCFData = 0,
            iCab = 1,
            iDisk = 0,
            fFailOnIncompressible = 0,
            setID = 0,
            szDisk = "",
            szCab = CabinetFileName,
            szCabPath = cabPath,
        };

        InitializeCallbacks();

        var erf = new NativeMethods.ERF();
        var hfci = NativeMethods.FCICreate(
            ref erf,
            _filePlacedCallback!,
            _allocCallback!,
            _freeCallback!,
            _openCallback!,
            _readCallback!,
            _writeCallback!,
            _closeCallback!,
            _seekCallback!,
            _deleteCallback!,
            _getTempFileCallback!,
            ref ccab,
            nint.Zero);

        if (hfci == nint.Zero)
            return Result<string>.Failure(
                ErrorKind.CompilationError,
                $"FCICreate failed. ERF: oper={erf.erfOper}, type={erf.erfType}");

        try
        {
            foreach (var file in files)
            {
                var success = NativeMethods.FCIAddFile(
                    hfci,
                    file.SourcePath,
                    file.FileName,
                    false,
                    _getNextCabinetCallback!,
                    _statusCallback!,
                    _getOpenInfoCallback!,
                    tcomp);

                if (!success)
                    return Result<string>.Failure(
                        ErrorKind.CompilationError,
                        $"FCIAddFile failed for '{file.SourcePath}'. ERF: oper={erf.erfOper}, type={erf.erfType}");
            }

            var flushed = NativeMethods.FCIFlushCabinet(
                hfci,
                false,
                _getNextCabinetCallback!,
                _statusCallback!);

            if (!flushed)
                return Result<string>.Failure(
                    ErrorKind.CompilationError,
                    $"FCIFlushCabinet failed. ERF: oper={erf.erfOper}, type={erf.erfType}");
        }
        finally
        {
            NativeMethods.FCIDestroy(hfci);
            CleanupOpenStreams();
        }

        var resultPath = Path.Combine(outputPath, CabinetFileName);
        if (!File.Exists(resultPath))
            return Result<string>.Failure(
                ErrorKind.CompilationError,
                $"Cabinet file was not created at expected path: {resultPath}");

        return resultPath;
    }

    private static ushort MapCompressionLevel(CompressionLevel level) => level switch
    {
        CompressionLevel.None => NativeMethods.TcompTypeNone,
        CompressionLevel.Low => NativeMethods.TcompTypeMszip,
        CompressionLevel.Medium => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowLo),
        CompressionLevel.High => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowHi),
        _ => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowHi),
    };

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
        _deleteCallback = CbDelete;
        _filePlacedCallback = CbFilePlaced;
        _getTempFileCallback = CbGetTempFile;
        _getNextCabinetCallback = CbGetNextCabinet;
        _statusCallback = CbStatus;
        _getOpenInfoCallback = CbGetOpenInfo;
    }

    public void Dispose()
    {
        CleanupOpenStreams();
    }

    private void CleanupOpenStreams()
    {
        foreach (var stream in _openStreams.Values)
        {
            stream.Dispose();
        }
        _openStreams.Clear();
    }

    // ── Callback implementations ────────────────────────────────────────

    private nint CbAlloc(uint cb)
    {
        return Marshal.AllocHGlobal((int)cb);
    }

    private void CbFree(nint memory)
    {
        Marshal.FreeHGlobal(memory);
    }

    private nint CbOpen(string pszFile, int oflag, int pmode, out int err, nint pv)
    {
        err = 0;
        try
        {
            // Map C-style open flags to .NET FileMode/FileAccess
            var (mode, access) = MapOpenFlags(oflag);
            var stream = new FileStream(pszFile, mode, access, FileShare.ReadWrite);
            var handle = (nint)_nextHandle++;
            _openStreams[handle] = stream;
            return handle;
        }
        catch
        {
            err = 1;
            return (nint)(-1);
        }
    }

    private uint CbRead(nint hf, nint memory, uint cb, out int err, nint pv)
    {
        err = 0;
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                err = 1;
                return unchecked((uint)-1);
            }

            var buffer = new byte[cb];
            var bytesRead = stream.Read(buffer, 0, (int)cb);
            Marshal.Copy(buffer, 0, memory, bytesRead);
            return (uint)bytesRead;
        }
        catch
        {
            err = 1;
            return unchecked((uint)-1);
        }
    }

    private uint CbWrite(nint hf, nint memory, uint cb, out int err, nint pv)
    {
        err = 0;
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                err = 1;
                return unchecked((uint)-1);
            }

            var buffer = new byte[cb];
            Marshal.Copy(memory, buffer, 0, (int)cb);
            stream.Write(buffer, 0, (int)cb);
            return cb;
        }
        catch
        {
            err = 1;
            return unchecked((uint)-1);
        }
    }

    private int CbClose(nint hf, out int err, nint pv)
    {
        err = 0;
        try
        {
            if (_openStreams.Remove(hf, out var stream))
            {
                stream.Dispose();
            }
            return 0;
        }
        catch
        {
            err = 1;
            return -1;
        }
    }

    private int CbSeek(nint hf, int dist, int seektype, out int err, nint pv)
    {
        err = 0;
        try
        {
            if (!_openStreams.TryGetValue(hf, out var stream))
            {
                err = 1;
                return -1;
            }

            var origin = seektype switch
            {
                0 => SeekOrigin.Begin,
                1 => SeekOrigin.Current,
                2 => SeekOrigin.End,
                _ => SeekOrigin.Begin,
            };

            var newPos = stream.Seek(dist, origin);
            return (int)newPos;
        }
        catch
        {
            err = 1;
            return -1;
        }
    }

    private static int CbDelete(string pszFile, out int err, nint pv)
    {
        err = 0;
        try
        {
            File.Delete(pszFile);
            return 0;
        }
        catch
        {
            err = 1;
            return -1;
        }
    }

    private static int CbFilePlaced(ref NativeMethods.CCAB pccab, string pszFile, long cbFile, int fContinuation, nint pv)
    {
        return 0; // Success
    }

    private static int CbGetTempFile(nint pszTempName, int cbTempName, nint pv)
    {
        try
        {
            var tempPath = Path.GetTempFileName();
            var bytes = System.Text.Encoding.ASCII.GetBytes(tempPath + '\0');
            if (bytes.Length > cbTempName)
                return 0; // FALSE - buffer too small

            Marshal.Copy(bytes, 0, pszTempName, bytes.Length);
            return 1; // TRUE
        }
        catch
        {
            return 0; // FALSE
        }
    }

    private static int CbGetNextCabinet(ref NativeMethods.CCAB pccab, uint cbPrevCab, nint pv)
    {
        // We don't support cabinet spanning
        return 0; // FALSE
    }

    private static int CbStatus(uint typeStatus, uint cb1, uint cb2, nint pv)
    {
        return 0; // Success, no progress reporting
    }

    private nint CbGetOpenInfo(
        string pszName,
        out ushort pdate,
        out ushort ptime,
        out ushort pattribs,
        out int err,
        nint pv)
    {
        err = 0;
        pdate = 0;
        ptime = 0;
        pattribs = 0;

        try
        {
            var info = new FileInfo(pszName);
            if (!info.Exists)
            {
                err = 1;
                return (nint)(-1);
            }

            // Convert to DOS date/time format
            var dt = info.LastWriteTime;
            pdate = ToDosDate(dt);
            ptime = ToDosTime(dt);
            pattribs = (ushort)(info.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive));

            // Open the file and return a handle
            var stream = new FileStream(pszName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var handle = (nint)_nextHandle++;
            _openStreams[handle] = stream;
            return handle;
        }
        catch
        {
            err = 1;
            return (nint)(-1);
        }
    }

    // ── DOS date/time helpers ───────────────────────────────────────────

    /// <summary>
    /// Converts a DateTime to DOS date format.
    /// Bits 15-9: year offset from 1980 (0-127), bits 8-5: month (1-12), bits 4-0: day (1-31).
    /// </summary>
    internal static ushort ToDosDate(DateTime dt)
    {
        return (ushort)(((dt.Year - 1980) << 9) | (dt.Month << 5) | dt.Day);
    }

    /// <summary>
    /// Converts a DateTime to DOS time format.
    /// Bits 15-11: hour (0-23), bits 10-5: minute (0-59), bits 4-0: seconds/2 (0-29).
    /// </summary>
    internal static ushort ToDosTime(DateTime dt)
    {
        return (ushort)((dt.Hour << 11) | (dt.Minute << 5) | (dt.Second / 2));
    }

    // ── C-style open flag mapping ───────────────────────────────────────

    private static (FileMode mode, FileAccess access) MapOpenFlags(int oflag)
    {
        // C runtime flags: _O_RDONLY=0, _O_WRONLY=1, _O_RDWR=2
        // _O_CREAT=0x100, _O_TRUNC=0x200, _O_BINARY=0x8000
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
            _ => FileAccess.ReadWrite,
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
