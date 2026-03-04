using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using FalkForge.Engine.Protocol.Bundle;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
///     Splits a FALKBUNDLE EXE into a bare PE stub and a data file for code signing,
///     then reattaches the signed stub with offset-patched TOC.
/// </summary>
public static class BundleDetacher
{
    private const int MagicLength = 16;
    private const int FooterLength = 24; // 16 (magic) + 8 (tocOffset)
    private const int CopyBufferSize = 64 * 1024;
    private const int MaxManifestScanSize = 16 * 1024 * 1024 + 20; // 16MB manifest + magic + length
    private static ReadOnlySpan<byte> Magic => PayloadEmbedder.BundleMagic;

    /// <summary>
    ///     Detaches a FALKBUNDLE into a bare PE stub and a data file.
    ///     The data file contains: [int64: originalStubSize][magic + manifest + payloads + TOC + footer].
    /// </summary>
    public static Result<Unit> Detach(string bundlePath, string stubPath, string dataPath)
    {
        if (!File.Exists(bundlePath))
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDS001: Bundle file not found: " + bundlePath);

        var stubTmpPath = stubPath + ".tmp";
        var dataTmpPath = dataPath + ".tmp";

        try
        {
            using var bundleStream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Read footer from EOF-24 to validate and get tocOffset
            if (bundleStream.Length < FooterLength)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS001: Not a valid FALKBUNDLE — file too small");

            bundleStream.Seek(-FooterLength, SeekOrigin.End);
            Span<byte> footerBuffer = stackalloc byte[FooterLength];
            bundleStream.ReadExactly(footerBuffer);

            if (!footerBuffer[..MagicLength].SequenceEqual(Magic))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS001: Not a valid FALKBUNDLE — footer magic not found");

            var tocOffset = BinaryPrimitives.ReadInt64LittleEndian(footerBuffer[MagicLength..]);

            // Read TOC entries to find the earliest payload offset
            bundleStream.Seek(tocOffset, SeekOrigin.Begin);
            Span<byte> countBuffer = stackalloc byte[4];
            bundleStream.ReadExactly(countBuffer);
            var entryCount = BinaryPrimitives.ReadInt32LittleEndian(countBuffer);

            if (entryCount < 0 || entryCount > 100_000)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS001: Not a valid FALKBUNDLE — invalid TOC entry count");

            // Read TOC entries using BinaryReader for string reads
            bundleStream.Seek(tocOffset + 4, SeekOrigin.Begin);
            using var tocReader = new BinaryReader(bundleStream, Encoding.UTF8, true);
            var entries = new TocEntry[entryCount];
            for (var i = 0; i < entryCount; i++)
                entries[i] = new TocEntry
                {
                    PackageId = tocReader.ReadString(),
                    Offset = tocReader.ReadInt64(),
                    CompressedSize = tocReader.ReadInt32(),
                    OriginalSize = tocReader.ReadInt32(),
                    Sha256Hash = tocReader.ReadString()
                };

            // Find stub size using footer-based backward scan
            var stubSize = FindStubSize(bundleStream, tocOffset, entries);
            if (stubSize < 0)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS001: Not a valid FALKBUNDLE — magic marker not found");

            // Write stub to temp file: bytes [0..stubSize)
            bundleStream.Seek(0, SeekOrigin.Begin);
            using (var stubStream = new FileStream(stubTmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                CopyBytes(bundleStream, stubStream, stubSize);
            }

            // Write data to temp file: [int64: stubSize] + [stubSize..EOF)
            bundleStream.Seek(stubSize, SeekOrigin.Begin);
            var dataLength = bundleStream.Length - stubSize;

            using (var dataStream = new FileStream(dataTmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(dataStream, Encoding.UTF8, true))
            {
                writer.Write(stubSize);
                CopyBytes(bundleStream, dataStream, dataLength);
            }

            // Atomic rename on success
            File.Move(stubTmpPath, stubPath, true);
            File.Move(dataTmpPath, dataPath, true);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupTempFile(stubTmpPath);
            CleanupTempFile(dataTmpPath);
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDS001: Failed to detach bundle: " + ex.Message);
        }
    }

    /// <summary>
    ///     Reattaches a signed PE stub with a detached data file, patching TOC offsets
    ///     to account for stub size changes from code signing.
    /// </summary>
    public static Result<Unit> Reattach(string signedStubPath, string dataPath, string outputPath)
    {
        if (!File.Exists(signedStubPath))
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDS002: Signed stub file not found: " + signedStubPath);

        if (!File.Exists(dataPath))
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDS002: Data file not found: " + dataPath);

        var outputTmpPath = outputPath + ".tmp";

        try
        {
            using var dataStream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var dataReader = new BinaryReader(dataStream, Encoding.UTF8, true);

            // Read original stub size from data header
            var originalStubSize = dataReader.ReadInt64();

            if (originalStubSize < 0)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS002: Data file corrupted — negative original stub size");

            // Validate magic at start of bundle data (right after header)
            Span<byte> magicCheck = stackalloc byte[MagicLength];
            dataStream.ReadExactly(magicCheck);
            if (!magicCheck.SequenceEqual(Magic))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS002: Data file is corrupted — magic marker not found after header");

            // Compute the bundle data length (everything after the 8-byte header in data file)
            var bundleDataLength = dataStream.Length - sizeof(long);

            // Read footer from end of data file: last 24 bytes of the bundle data portion
            // Data file layout: [8 bytes header][bundleData...]
            // Footer is at: dataStream.Length - FooterLength
            dataStream.Seek(-FooterLength, SeekOrigin.End);
            Span<byte> footerMagic = stackalloc byte[MagicLength];
            dataStream.ReadExactly(footerMagic);
            if (!footerMagic.SequenceEqual(Magic))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS002: Data file footer is corrupted");

            var originalTocOffset = dataReader.ReadInt64();

            if (originalTocOffset < originalStubSize + MagicLength)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS002: Data file corrupted — TOC offset precedes bundle data");

            // Compute where TOC is within the data file
            // originalTocOffset is absolute in the original bundle
            // In data file: offset = 8 (header) + (originalTocOffset - originalStubSize)
            var tocOffsetInDataFile = sizeof(long) + (originalTocOffset - originalStubSize);

            // Read TOC entries from data file
            dataStream.Seek(tocOffsetInDataFile, SeekOrigin.Begin);
            var entryCount = dataReader.ReadInt32();

            if (entryCount < 0 || entryCount > 100_000)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDS002: Data file corrupted — invalid TOC entry count");

            var entries = new TocEntry[entryCount];
            for (var i = 0; i < entryCount; i++)
                entries[i] = new TocEntry
                {
                    PackageId = dataReader.ReadString(),
                    Offset = dataReader.ReadInt64(),
                    CompressedSize = dataReader.ReadInt32(),
                    OriginalSize = dataReader.ReadInt32(),
                    Sha256Hash = dataReader.ReadString()
                };

            // Determine signed stub size and offset delta
            var newStubSize = new FileInfo(signedStubPath).Length;
            var delta = newStubSize - originalStubSize;

            // Validate payload offsets after patching
            foreach (var entry in entries)
            {
                var patchedOffset = entry.Offset + delta;
                if (patchedOffset < newStubSize)
                    return Result<Unit>.Failure(ErrorKind.BundleError,
                        $"BDS003: Reattach verification failed — payload '{entry.PackageId}' offset out of bounds");
            }

            // Build output to temp file: signed stub + bundle data (magic through payloads) + patched TOC + new footer
            using (var outputStream = new FileStream(outputTmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // 1. Copy signed stub
                using (var stubStream = new FileStream(signedStubPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CopyBytes(stubStream, outputStream, stubStream.Length);
                }

                // 2. Copy bundle data from magic through end of payloads (before TOC)
                // In data file: magic starts at offset 8, payloads end at tocOffsetInDataFile
                var preTocLength = tocOffsetInDataFile - sizeof(long);
                dataStream.Seek(sizeof(long), SeekOrigin.Begin);
                CopyBytes(dataStream, outputStream, preTocLength);

                // 3. Write patched TOC
                var newTocOffset = outputStream.Position;
                using var outputWriter = new BinaryWriter(outputStream, Encoding.UTF8, true);
                outputWriter.Write(entryCount);
                foreach (var entry in entries)
                {
                    outputWriter.Write(entry.PackageId);
                    outputWriter.Write(entry.Offset + delta);
                    outputWriter.Write(entry.CompressedSize);
                    outputWriter.Write(entry.OriginalSize);
                    outputWriter.Write(entry.Sha256Hash);
                }

                // 4. Write footer
                outputWriter.Write(PayloadEmbedder.BundleMagic.ToArray());
                outputWriter.Write(newTocOffset);
                outputWriter.Flush();

                // 5. In-memory verification
                var fileLength = outputStream.Length;
                if (newTocOffset < 0 || newTocOffset >= fileLength)
                    return Result<Unit>.Failure(ErrorKind.BundleError,
                        "BDS003: Reattach verification failed — TOC offset out of bounds");

                // Validate patched payload offsets are within bounds
                foreach (var entry in entries)
                {
                    var patchedOffset = entry.Offset + delta;
                    if (patchedOffset < newStubSize || patchedOffset >= newTocOffset)
                        return Result<Unit>.Failure(ErrorKind.BundleError,
                            $"BDS003: Reattach verification failed — payload '{entry.PackageId}' offset out of bounds");
                }
            }

            // Atomic rename on success
            File.Move(outputTmpPath, outputPath, true);

            return Unit.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            CleanupTempFile(outputTmpPath);
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDS003: Failed to reattach bundle: " + ex.Message);
        }
    }

    /// <summary>
    ///     Finds the stub size by scanning backward from the first data point after the manifest
    ///     for the 16-byte FALKBUNDLE magic marker. Validates by checking that the manifest length
    ///     field following the magic is consistent with the known data layout.
    /// </summary>
    private static long FindStubSize(Stream stream, long tocOffset, TocEntry[] entries)
    {
        // The first byte of payload data (or TOC if no payloads) is immediately after the manifest
        var dataStart = entries.Length > 0
            ? entries.Min(e => e.Offset)
            : tocOffset;

        // The magic + manifestLength + manifest must fit between stubSize and dataStart
        // Scan backward from dataStart for the magic, limited to MaxManifestScanSize
        var scanSize = (int)Math.Min(dataStart, MaxManifestScanSize);
        var scanStart = dataStart - scanSize;

        var buffer = ArrayPool<byte>.Shared.Rent(scanSize);
        try
        {
            stream.Seek(scanStart, SeekOrigin.Begin);
            stream.ReadExactly(buffer, 0, scanSize);

            var magicBytes = PayloadEmbedder.BundleMagic;

            // Scan backward for magic
            for (var i = scanSize - MagicLength; i >= 0; i--)
                if (buffer.AsSpan(i, MagicLength).SequenceEqual(magicBytes))
                    // Validate: next 4 bytes = manifestLength, and offset + 20 + manifestLength == dataStart
                    if (i + 20 <= scanSize)
                    {
                        var manifestLen = BinaryPrimitives.ReadInt32LittleEndian(
                            buffer.AsSpan(i + MagicLength, 4));
                        var expectedDataStart = scanStart + i + 20 + manifestLen;
                        if (manifestLen >= 0 && expectedDataStart == dataStart)
                            return scanStart + i; // This is stubSize
                    }

            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     Copies exactly <paramref name="count" /> bytes from source to destination using pooled buffered I/O.
    /// </summary>
    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            var remaining = count;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, CopyBufferSize);
                var bytesRead = source.Read(buffer, 0, toRead);
                if (bytesRead == 0)
                    throw new EndOfStreamException("Unexpected end of stream during copy");

                destination.Write(buffer, 0, bytesRead);
                remaining -= bytesRead;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     Attempts to delete a temporary file, suppressing any exceptions.
    /// </summary>
    private static void CleanupTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; suppress exceptions
        }
    }
}