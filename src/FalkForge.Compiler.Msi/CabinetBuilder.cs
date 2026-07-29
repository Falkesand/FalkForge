using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Interop;
using FalkForge.Diagnostics;

namespace FalkForge.Compiler.Msi;

// Split across partial-class files, mirroring CabinetExtractor: this file holds construction,
// BuildCabinet, and the small mapping helpers it calls directly; CabinetBuilder.Callbacks.cs
// holds the FCI callback implementations (and their C-style open-flag / DOS date-time helpers).
[SupportedOSPlatform("windows")]
public sealed partial class CabinetBuilder : IDisposable
{
    // CabinetPlanner.DefaultCabinetFileName is the cross-platform source of
    // truth; this alias keeps existing callers compiled without changes.
    public const string DefaultCabinetFileName = CabinetPlanner.DefaultCabinetFileName;

    private readonly DateTime? _normalizedTimestamp;
    private readonly IFalkLogger? _logger;
    private readonly bool _captureSha1;

    // File handle tracking: maps pseudo-handles to FileStream instances.
    // FCI callbacks use these to perform file I/O through managed streams.
    private readonly Dictionary<nint, FileStream> _openStreams = new();

    // Tracks the in-flight digests for every handle CbGetOpenInfo opened (i.e. every source file
    // FCIAddFile is reading to compress into the cabinet), keyed by the pseudo-handle CbRead sees.
    // CbClose finalizes them into _packagedFileHashes / _packagedFileSha1Hashes below. This is the
    // sole point where the MSI's packaged bytes are actually consumed, so it is the only place a
    // digest can be captured without a TOCTOU gap against a later SBOM-writing step reopening
    // the source path (see SbomHelper.WriteSbomSidecar). Both algorithms are fed from the one
    // CbRead chunk, so the SHA-1 costs no extra read and cannot describe a different byte stream
    // than the SHA-256 does.
    private readonly Dictionary<nint, PendingDigests> _pendingSourceHashes = new();

    // SHA-256 digest (uppercase hex) of every source file's bytes as CbRead actually consumed
    // them, keyed by ResolvedFile.FileId — the MSI File table's own unique identity, not
    // SourcePath: two File entries can legitimately share a source path (the same binary shipped
    // into two components/destinations), and keying on the path would let the second FCIAddFile
    // call collapse onto the first entry's digest. Populated incrementally as each file's handle
    // is closed during BuildCabinet; complete once BuildCabinet returns.
    private readonly Dictionary<string, string> _packagedFileHashes = new(StringComparer.Ordinal);

    // SHA-1 counterpart of _packagedFileHashes, over the identical byte stream. See
    // PackagedFileSha1Hashes for why a broken hash is captured at all and what it may not be used for.
    private readonly Dictionary<string, string> _packagedFileSha1Hashes = new(StringComparer.Ordinal);

    // The FileId of the ResolvedFile currently being handed to FCIAddFile. FCI invokes
    // CbGetOpenInfo synchronously within that call to open the source file being added, so this
    // field lets that callback recover the packaged entry's identity — pszName alone is just the
    // source path, which is not guaranteed unique across entries.
    private string _currentFileId = string.Empty;

    // Pinned callback delegates - must survive until FCIDestroy completes
    private NativeMethods.FnFciAlloc? _allocCallback;
    private NativeMethods.FnFciClose? _closeCallback;
    private NativeMethods.FnFciDelete? _deleteCallback;
    private NativeMethods.FnFciFilePlaced? _filePlacedCallback;
    private NativeMethods.FnFciFree? _freeCallback;
    private NativeMethods.FnFciGetNextCabinet? _getNextCabinetCallback;
    private NativeMethods.FnFciGetOpenInfo? _getOpenInfoCallback;
    private NativeMethods.FnFciGetTempFile? _getTempFileCallback;
    private int _nextHandle = 1;
    private NativeMethods.FnFciOpen? _openCallback;
    private NativeMethods.FnFciRead? _readCallback;
    private NativeMethods.FnFciSeek? _seekCallback;
    private NativeMethods.FnFciStatus? _statusCallback;
    private NativeMethods.FnFciWrite? _writeCallback;

    /// <param name="normalizedTimestamp">
    /// Optional fixed timestamp applied to every cabinet entry for reproducible builds.
    /// </param>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every
    /// existing caller compiles and behaves unchanged.
    /// </param>
    /// <param name="captureSha1">
    /// Whether to accumulate <see cref="PackagedFileSha1Hashes"/> alongside the SHA-256 map.
    /// <see langword="true"/> by default so an existing caller — and any future one that does not
    /// think about it — gets the digest rather than silently losing it.
    ///
    /// <para>SHA-1 exists here solely to satisfy SPDX 2.3 §8.4, and SPDX output is opt-in
    /// (<c>Integrity()</c> defaults to CycloneDX, which ignores the field). Hashing every packaged
    /// byte a second time on every compile for a format almost no compile requests is waste
    /// <c>MsiAuthoring.BuildCabinetsAndEmbed</c> declines by passing <see langword="false"/> unless
    /// the package actually asked for SPDX. The saving is ~1% under LZX-High and materially more
    /// under <c>CompressionLevel.None</c>, where there is no compressor in the denominator.</para>
    ///
    /// <para>This flag carries no knowledge of SBOMs into the FCI callbacks: it is a plain
    /// "accumulate the second digest or don't", and the SHA-256 accumulation the ECDSA payload
    /// manifest signs is identical either way.</para>
    /// </param>
    public CabinetBuilder(
        DateTime? normalizedTimestamp = null, IFalkLogger? logger = null, bool captureSha1 = true)
    {
        _normalizedTimestamp = normalizedTimestamp;
        _logger = logger;
        _captureSha1 = captureSha1;
    }

    /// <summary>
    /// The digests being accumulated for one in-flight source file, plus the
    /// <see cref="ResolvedFile.FileId"/> they will be filed under once <c>CbClose</c> finalizes them.
    /// A struct, not a tuple, so the two <see cref="IncrementalHash"/> instances are named at every
    /// use site — mixing them up would silently file a SHA-1 as a SHA-256, which the ECDSA payload
    /// manifest signs.
    ///
    /// <para><see cref="Sha1"/> is nullable because it is only accumulated when SPDX output was
    /// requested (see the <c>captureSha1</c> constructor parameter). <see cref="Sha256"/> is not:
    /// it is the digest the signature commits to and is unconditional on every path.</para>
    /// </summary>
    private readonly record struct PendingDigests(string FileId, IncrementalHash Sha256, IncrementalHash? Sha1)
    {
        internal void Append(byte[] buffer, int offset, int count)
        {
            Sha256.AppendData(buffer, offset, count);
            Sha1?.AppendData(buffer, offset, count);
        }

        // Not named Dispose: PendingDigests is a value type that deliberately does not implement
        // IDisposable (it is copied out of the dictionary by value), and a Dispose-shaped method on
        // a non-IDisposable type reads as an ownership contract it does not have.
        internal void DisposeHashes()
        {
            Sha256.Dispose();
            Sha1?.Dispose();
        }
    }

    public void Dispose()
    {
        CleanupOpenStreams();
    }

    /// <summary>
    /// SHA-256 digest (uppercase hex) of every source file's bytes as the native FCI compressor
    /// actually read them while building this cabinet, keyed by <see cref="ResolvedFile.FileId"/>.
    /// Captured at the point the packaged bytes are consumed (CbGetOpenInfo opens the handle,
    /// CbRead feeds every chunk into the digest, CbClose finalizes it) rather than by reopening
    /// the source path afterwards — a source file edited between "cabinet built" and "SBOM
    /// written" therefore cannot desync the recorded digest from what actually shipped. Keyed by
    /// FileId rather than SourcePath so two File entries sharing a source path each keep their own
    /// digest. Populated once <see cref="BuildCabinet"/> returns.
    /// </summary>
    public IReadOnlyDictionary<string, string> PackagedFileHashes => _packagedFileHashes;

    /// <summary>
    /// SHA-1 digest (uppercase hex) of the same packaged byte stream <see cref="PackagedFileHashes"/>
    /// covers, keyed identically by <see cref="ResolvedFile.FileId"/>. Accumulated in the very same FCI
    /// callbacks — one <c>CbRead</c> chunk feeds both digests — so it inherits the TOCTOU guarantee for
    /// free: there is no second pass over the source file that a racing writer could slip between.
    ///
    /// <para><b>Why a broken hash is captured at all.</b> SPDX 2.3 §8.4 fixes the cardinality of a
    /// file's checksum at "1..1 for the SHA1 algorithm, 0..* for all other algorithms" — a SPDX
    /// document without a per-file SHA1 is not a valid SPDX document, whatever else it carries. This
    /// value exists solely to satisfy that identifier requirement (and the package verification code
    /// derived from it, SPDX 2.3 §7.9).</para>
    ///
    /// <para><b>What it must never be used for.</b> SHA-1 is collision-broken and nothing in FalkForge
    /// makes a trust decision on it. The ECDSA payload manifest signs
    /// <see cref="PackagedFileHashes"/> (SHA-256) and <c>MsiIntegrityVerifier</c> re-verifies against
    /// SHA-256; this map feeds descriptive SBOM output only. Do not route it into a signature, a
    /// comparison that gates installation, or any tamper check.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> PackagedFileSha1Hashes => _packagedFileSha1Hashes;

    public Result<string> BuildCabinet(
        IReadOnlyList<ResolvedFile> files,
        string outputPath,
        CompressionLevel compression,
        string cabinetFileName = DefaultCabinetFileName)
    {
        // Level-guarded: this runs once per cabinet, potentially many per compile, so avoid
        // the interpolated message allocation unless Debug logging is actually enabled (D2/D6).
        if (_logger is not null && _logger.MinimumLevel <= LogLevel.Debug)
            _logger.Debug("CabinetBuilder", $"Building cabinet '{cabinetFileName}' with {files.Count} file(s).");

        if (files.Count == 0)
        {
            _logger?.Error("CabinetBuilder", $"Cannot build cabinet '{cabinetFileName}' with no files.");
            return Result<string>.Failure(ErrorKind.InvalidOperation, "Cannot build a cabinet with no files.");
        }

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
            szCab = cabinetFileName,
            szCabPath = cabPath
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
        {
            _logger?.Error("CabinetBuilder", $"FCICreate failed for '{cabinetFileName}'. ERF: oper={erf.erfOper}, type={erf.erfType}");
            return Result<string>.Failure(
                ErrorKind.CompilationError,
                $"FCICreate failed. ERF: oper={erf.erfOper}, type={erf.erfType}");
        }

        try
        {
            foreach (var file in files)
            {
                // MSI looks up cabinet entries by the File table's File key (the sanitized
                // FileId), not the on-disk source filename, so the in-cabinet name must be
                // the FileId. Otherwise the installer aborts with error 1334 'file cannot
                // be found in cabinet' whenever the two differ.
                //
                // FCIAddFile invokes CbGetOpenInfo synchronously to open file.SourcePath, so
                // recording the FileId here — just before the call — lets that callback tag
                // the digest it starts with this entry's real identity (see _currentFileId).
                _currentFileId = file.FileId;
                var success = NativeMethods.FCIAddFile(
                    hfci,
                    file.SourcePath,
                    file.FileId,
                    false,
                    _getNextCabinetCallback!,
                    _statusCallback!,
                    _getOpenInfoCallback!,
                    tcomp);

                if (!success)
                {
                    _logger?.Error("CabinetBuilder", $"FCIAddFile failed for '{file.SourcePath}'. ERF: oper={erf.erfOper}, type={erf.erfType}");
                    return Result<string>.Failure(
                        ErrorKind.CompilationError,
                        $"FCIAddFile failed for '{file.SourcePath}'. ERF: oper={erf.erfOper}, type={erf.erfType}");
                }
            }

            var flushed = NativeMethods.FCIFlushCabinet(
                hfci,
                false,
                _getNextCabinetCallback!,
                _statusCallback!);

            if (!flushed)
            {
                _logger?.Error("CabinetBuilder", $"FCIFlushCabinet failed for '{cabinetFileName}'. ERF: oper={erf.erfOper}, type={erf.erfType}");
                return Result<string>.Failure(
                    ErrorKind.CompilationError,
                    $"FCIFlushCabinet failed. ERF: oper={erf.erfOper}, type={erf.erfType}");
            }
        }
        finally
        {
            NativeMethods.FCIDestroy(hfci);
            CleanupOpenStreams();

            // The FCI callbacks are rooted in instance fields, so they live exactly as
            // long as 'this'. But nothing in this finally block (or FCIDestroy, which
            // takes only the raw handle) references 'this' — the JIT could therefore
            // collect this CabinetBuilder while FCIDestroy is still invoking the close
            // callback natively, crashing the process with "A callback was made on a
            // garbage collected delegate". Keep the instance — and through it every
            // rooted delegate field — alive until after the last native call.
            GC.KeepAlive(this);
        }

        var resultPath = Path.Combine(outputPath, cabinetFileName);
        if (!File.Exists(resultPath))
        {
            _logger?.Error("CabinetBuilder", $"Cabinet file was not created at expected path: {resultPath}");
            return Result<string>.Failure(
                ErrorKind.CompilationError,
                $"Cabinet file was not created at expected path: {resultPath}");
        }

        return resultPath;
    }

    private static ushort MapCompressionLevel(CompressionLevel level)
    {
        return level switch
        {
            CompressionLevel.None => NativeMethods.TcompTypeNone,
            CompressionLevel.Low => NativeMethods.TcompTypeMszip,
            CompressionLevel.Medium => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowLo),
            CompressionLevel.High => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowHi),
            _ => NativeMethods.TcompLzxWindow(NativeMethods.TcompLzxWindowHi)
        };
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
        _deleteCallback = CbDelete;
        _filePlacedCallback = CbFilePlaced;
        _getTempFileCallback = CbGetTempFile;
        _getNextCabinetCallback = CbGetNextCabinet;
        _statusCallback = CbStatus;
        _getOpenInfoCallback = CbGetOpenInfo;
    }

    private void CleanupOpenStreams()
    {
        foreach (var stream in _openStreams.Values) stream.Dispose();
        _openStreams.Clear();

        // Any handle still pending here means CbClose never ran for it (e.g. an FCI failure
        // path) — its digests are incomplete, so dispose without finalizing into either map.
        foreach (var tracked in _pendingSourceHashes.Values) tracked.DisposeHashes();
        _pendingSourceHashes.Clear();
    }
}
