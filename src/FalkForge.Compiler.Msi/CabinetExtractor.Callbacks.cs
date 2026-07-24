using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed partial class CabinetExtractor
{
    // ── Callback implementations ────────────────────────────────────────

    private nint CbOpen(string pszFile, int oflag, int pmode)
    {
        try
        {
            var (mode, access) = CabinetCallbackShim.MapOpenFlags(oflag);
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
}
