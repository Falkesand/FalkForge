namespace FalkForge.Engine.Protocol.Integrity;

using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Opens a file, hashes its bytes, and hands the caller the still-open handle so the bytes that
/// were hashed cannot be replaced before they are used.
///
/// <para><b>Why the handle stays open.</b> Both elevation crossings — the elevated MSI install
/// command and the pre-UI prerequisite launcher — used to hash a file and then hand the path to a
/// consumer that opens it again. Between those two opens a same-user process can overwrite the
/// file, so the bytes that were checked are not necessarily the bytes that run. Opening once with
/// <see cref="FileShare.Read"/> and holding that handle for the whole operation denies every other
/// process write, rename and delete access for the duration, which closes that window.</para>
///
/// <para><b>Disposal contract.</b> On <see cref="HashBoundFileStatus.Verified"/> the caller owns
/// the returned stream and must dispose it after the consumer is finished. On every other status
/// the helper has already disposed whatever it opened, and
/// <see cref="HashBoundFileResult.Stream"/> is <see langword="null"/>. Expected filesystem
/// failures are reported as a status rather than thrown; anything else propagates, and the outer
/// <c>finally</c> disposes the handle on the way out.</para>
/// </summary>
public static class HashBoundFile
{
    /// <summary>
    /// The longest path the classic Win32 consumers of a resolved path accept: <c>MAX_PATH</c>
    /// (260) minus the terminating null. Both <c>MsiInstallProductW</c> and
    /// <c>CreateProcessW</c> are bounded by it, and neither takes the <c>\\?\</c> form that would
    /// lift the bound. A caller that resolves a longer path must fail closed rather than fall
    /// back to the unresolved one.
    /// </summary>
    public const int MaxLegacyPathLength = 259;

    private const int Sha256ByteLength = 32;

    /// <summary>
    /// 80 KiB, the same size <see cref="Stream.CopyTo(Stream)"/> uses. Rented from
    /// <see cref="ArrayPool{T}"/> so repeated calls do not push large arrays onto the heap.
    /// </summary>
    private const int ReadBufferLength = 81920;

    /// <summary>
    /// Opens <paramref name="path"/> for shared read, hashes every byte, and compares the digest
    /// against <paramref name="expectedHashHex"/>.
    /// </summary>
    /// <param name="path">Full path of the file to open and verify.</param>
    /// <param name="expectedHashHex">
    /// The expected SHA-256 digest as 64 hexadecimal characters. Anything else is rejected as
    /// <see cref="HashBoundFileStatus.MalformedExpectedHash"/> before the file is touched.
    /// </param>
    /// <returns>
    /// A <see cref="HashBoundFileResult"/> carrying the open handle on success, and a status plus
    /// plain-language detail on failure. See the disposal contract on <see cref="HashBoundFile"/>.
    /// </returns>
    public static HashBoundFileResult Open(string path, string expectedHashHex)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expectedHashHex);

        // Convert has no TryFromHexString overload on .NET 10 — only the throwing
        // FromHexString(string) and this OperationStatus-returning span overload. BOTH halves of
        // the guard below are load-bearing and neither can be dropped:
        //   * written != 32 catches empty, short and non-hex input.
        //   * status != Done catches input LONGER than 64 characters, where the first 64
        //     characters fill the destination and decoding stops with written == 32. A 65-char
        //     value returns NeedMoreData and a 66-char value returns DestinationTooSmall, both
        //     with written == 32, so without the status check a hash with trailing junk would
        //     silently truncate to a valid digest.
        Span<byte> expectedHash = stackalloc byte[Sha256ByteLength];
        var hexStatus = Convert.FromHexString(expectedHashHex, expectedHash, out _, out var written);
        if (hexStatus != OperationStatus.Done || written != Sha256ByteLength)
            return new HashBoundFileResult(HashBoundFileStatus.MalformedExpectedHash, null, null, null);

        // stream is nulled out on the success path to hand ownership to the caller, so the outer
        // finally disposes it on every failing return AND on any exception that escapes.
        FileStream? stream = null;
        try
        {
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new HashBoundFileResult(HashBoundFileStatus.FileNotFound, null, null, ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new HashBoundFileResult(HashBoundFileStatus.OpenFailed, null, null, ex.Message);
            }

            Span<byte> actualHash = stackalloc byte[Sha256ByteLength];
            try
            {
                ComputeSha256(stream, actualHash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Reachable: FileStream.Read throws IOException on a device or network error
                // part-way through a large file. Without this the handle would leak.
                return new HashBoundFileResult(HashBoundFileStatus.ReadFailed, null, null, ex.Message);
            }

            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                return new HashBoundFileResult(
                    HashBoundFileStatus.HashMismatch, null, null, Convert.ToHexString(actualHash));

            // Ask Windows which file this handle refers to, and hand THAT path on rather than the
            // one the caller supplied. The handle pins the file object, not the reparse points in
            // the path that reached it: a same-user attacker can rename a directory, put a
            // junction in its place, and repoint that junction while the hash runs, with no
            // privilege at all. Every re-open of the caller-supplied string then lands on the
            // attacker's file. The resolved path contains no reparse points, so re-opening it
            // cannot be redirected.
            var finalPath = TryGetFinalPath(stream.SafeFileHandle);
            if (finalPath is null)
                return new HashBoundFileResult(HashBoundFileStatus.PathResolutionFailed, null, null, null);

            var verified = new HashBoundFileResult(HashBoundFileStatus.Verified, stream, finalPath, null);
            stream = null;
            return verified;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    /// <summary>
    /// Asks Windows for the path of the file <paramref name="handle"/> refers to, with every
    /// reparse point already followed and every short (8.3) name already expanded, and returns it
    /// in the ordinary drive-letter or UNC form.
    /// </summary>
    /// <param name="handle">An open file handle.</param>
    /// <returns>
    /// The resolved path, or <see langword="null"/> when Windows could not name the file -- which
    /// happens for a volume with no drive letter, and for a handle to something that is not a
    /// file on a volume. Callers must fail closed on <see langword="null"/>: falling back to the
    /// caller-supplied path would reinstate exactly the redirection this call defeats.
    /// </returns>
    public static string? TryGetFinalPath(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        // FILE_NAME_NORMALIZED (0x0) | VOLUME_NAME_DOS (0x0). Named rather than inlined as 0 so
        // the intent is readable: normalized long names, drive-letter volume form.
        const uint FileNameNormalizedVolumeNameDos = 0x0;

        // 300 chars covers MAX_PATH plus the \\?\ or \\?\UNC\ prefix without touching the heap.
        Span<char> buffer = stackalloc char[300];
        var length = NativeFinalPathMethods.GetFinalPathNameByHandle(
            handle, ref MemoryMarshal.GetReference(buffer), (uint)buffer.Length, FileNameNormalizedVolumeNameDos);

        if (length == 0)
            return null;

        // On success the return value excludes the terminating null, so it is strictly less than
        // the buffer length. Anything else means the buffer was too small and the return value is
        // the required size INCLUDING that null.
        if (length < (uint)buffer.Length)
            return StripExtendedLengthPrefix(buffer[..(int)length]);

        var rented = ArrayPool<char>.Shared.Rent((int)length);
        try
        {
            var grown = rented.AsSpan(0, (int)length);
            var grownLength = NativeFinalPathMethods.GetFinalPathNameByHandle(
                handle, ref MemoryMarshal.GetReference(grown), (uint)grown.Length, FileNameNormalizedVolumeNameDos);

            if (grownLength == 0 || grownLength >= (uint)grown.Length)
                return null;

            return StripExtendedLengthPrefix(grown[..(int)grownLength]);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Turns the extended-length form Windows returns into the ordinary form. Neither
    /// <c>MsiInstallProductW</c> nor <see cref="System.Diagnostics.ProcessStartInfo.FileName"/>
    /// accepts a <c>\\?\</c> path. The UNC form comes back as <c>\\?\UNC\server\share\...</c> and
    /// has to become <c>\\server\share\...</c>, not <c>\UNC\...</c>.
    /// </summary>
    private static string StripExtendedLengthPrefix(ReadOnlySpan<char> path)
    {
        const string ExtendedUncPrefix = @"\\?\UNC\";
        const string ExtendedPrefix = @"\\?\";

        if (path.StartsWith(ExtendedUncPrefix, StringComparison.Ordinal))
            return string.Concat(@"\\", path[ExtendedUncPrefix.Length..]);

        if (path.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
            return new string(path[ExtendedPrefix.Length..]);

        return new string(path);
    }

    private static void ComputeSha256(Stream source, Span<byte> destination)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferLength);
        try
        {
            int bytesRead;
            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                hasher.AppendData(buffer.AsSpan(0, bytesRead));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        hasher.GetHashAndReset(destination);
    }
}
