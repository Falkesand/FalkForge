namespace FalkForge.Engine.Protocol.Integrity;

using System.Buffers;
using System.Security.Cryptography;

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

            var verified = new HashBoundFileResult(HashBoundFileStatus.Verified, stream, path, null);
            stream = null;
            return verified;
        }
        finally
        {
            stream?.Dispose();
        }
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
