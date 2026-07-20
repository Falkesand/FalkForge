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
    private static ReadOnlySpan<byte> Magic => BundleReader.BundleMagic;

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
            {
                var packageId = tocReader.ReadString();
                var offset = tocReader.ReadInt64();
                var compressedSize = tocReader.ReadInt32();
                var originalSize = tocReader.ReadInt32();
                var sha256Hash = tocReader.ReadString();

                var isDelta = false;
                var isPreUI = false;
                string? baseSha256Hash = null;
                string? reconstructedSha256Hash = null;

                var flags = tocReader.ReadByte();
                isDelta = (flags & 0x01) != 0;
                isPreUI = (flags & 0x02) != 0;

                if (isDelta)
                {
                    baseSha256Hash = tocReader.ReadString();
                    reconstructedSha256Hash = tocReader.ReadString();
                }

                entries[i] = new TocEntry
                {
                    PackageId = packageId,
                    Offset = offset,
                    CompressedSize = compressedSize,
                    OriginalSize = originalSize,
                    Sha256Hash = sha256Hash,
                    IsDelta = isDelta,
                    BaseSha256Hash = baseSha256Hash,
                    ReconstructedSha256Hash = reconstructedSha256Hash,
                    IsPreUI = isPreUI
                };
            }

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
            {
                var packageId = dataReader.ReadString();
                var offset = dataReader.ReadInt64();
                var compressedSize = dataReader.ReadInt32();
                var originalSize = dataReader.ReadInt32();
                var sha256Hash = dataReader.ReadString();

                var isDelta = false;
                var isPreUI = false;
                string? baseSha256Hash = null;
                string? reconstructedSha256Hash = null;

                var flags = dataReader.ReadByte();
                isDelta = (flags & 0x01) != 0;
                isPreUI = (flags & 0x02) != 0;

                if (isDelta)
                {
                    baseSha256Hash = dataReader.ReadString();
                    reconstructedSha256Hash = dataReader.ReadString();
                }

                entries[i] = new TocEntry
                {
                    PackageId = packageId,
                    Offset = offset,
                    CompressedSize = compressedSize,
                    OriginalSize = originalSize,
                    Sha256Hash = sha256Hash,
                    IsDelta = isDelta,
                    BaseSha256Hash = baseSha256Hash,
                    ReconstructedSha256Hash = reconstructedSha256Hash,
                    IsPreUI = isPreUI
                };
            }

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
                    // Flags byte (bit field): bit 0 = IsDelta, bit 1 = IsPreUI
                    byte entryFlags = 0;
                    if (entry.IsDelta) entryFlags |= 0x01;
                    if (entry.IsPreUI) entryFlags |= 0x02;
                    outputWriter.Write(entryFlags);
                    if (entry.IsDelta)
                    {
                        outputWriter.Write(entry.BaseSha256Hash ?? string.Empty);
                        outputWriter.Write(entry.ReconstructedSha256Hash ?? string.Empty);
                    }
                }

                // 3b. Align the file to an 8-byte boundary before the footer. WinVerifyTrust
                // requires the PE attribute-certificate table to be a multiple of 8 bytes, and
                // PreserveAuthenticodeSignature below extends that table to EOF — so a signed stub's
                // signature only survives when EOF sits on an 8-byte boundary (the cert table starts
                // 8-aligned). The footer is 24 bytes (itself a multiple of 8), so aligning the
                // pre-footer position aligns EOF. These padding bytes live between the TOC and the
                // footer and are never read by the reader (it locates the TOC via the footer's
                // absolute offset), so they are inert for unsigned bundles too.
                outputWriter.Flush();
                var padCount = (int)((8 - outputStream.Position % 8) % 8);
                for (var p = 0; p < padCount; p++)
                    outputWriter.Write((byte)0);

                // 4. Write footer
                outputWriter.Write(BundleReader.BundleMagic.ToArray());
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

            // Preserve the signed stub's Authenticode signature across the append. The bundle data
            // was written PAST the PE attribute-certificate table, which by itself pushes the cert
            // table off EOF and makes Windows report the whole file as unsigned (TRUST_E_NOSIGNATURE).
            // Extending the PE Security data-directory (and the last WIN_CERTIFICATE) to span the
            // appended bytes puts the cert table back at EOF without touching any signed bytes — the
            // signature remains valid. No-op (and never a failure) when the stub is not a signed PE.
            PreserveAuthenticodeSignature(outputTmpPath, newStubSize);

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
    ///     Restores the Authenticode signature of a reattached bundle whose signed PE stub had its
    ///     attribute-certificate table pushed off EOF by the appended bundle data.
    ///     <para>
    ///     Authenticode locates the certificate table via the PE optional header's
    ///     <c>IMAGE_DIRECTORY_ENTRY_SECURITY</c> data directory (VirtualAddress = the table's file
    ///     offset, Size = its byte length) and — critically — EXCLUDES that entire region from the
    ///     signed digest. So growing the Security directory's <c>Size</c> to reach the new EOF, and
    ///     the trailing <c>WIN_CERTIFICATE.dwLength</c> to match, makes the appended bundle bytes
    ///     part of the (unhashed) certificate region. Neither patched field is part of the signed
    ///     digest (the 8-byte Security directory entry and the whole cert region are both skipped),
    ///     so the signature the publisher applied to the bare stub still verifies over the reattached
    ///     file. The trailing bundle bytes sit inside the enlarged last WIN_CERTIFICATE as content
    ///     past the PKCS#7 DER structure, which the signature decoder ignores.
    ///     </para>
    ///     <para>
    ///     Purely a byte-level PE patch — no reflection or dynamic code — so it is safe under
    ///     NativeAOT. It is a best-effort no-op when the stub is not a signed PE (an unsigned or
    ///     non-PE stub, or a layout where the cert table was not exactly at the stub's EOF): in
    ///     those cases the reattached bundle simply carries no Authenticode signature, exactly as
    ///     before this method existed. It never alters bytes that are part of the signed digest.
    ///     </para>
    /// </summary>
    private static void PreserveAuthenticodeSignature(string filePath, long signedStubSize)
    {
        const int DosLfanewOffset = 0x3C;
        const int PeSignatureSize = 4; // "PE\0\0"
        const int CoffHeaderSize = 20;
        const int SecurityDirectoryIndex = 4;
        const int DataDirectoryEntrySize = 8; // VirtualAddress (4) + Size (4)

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var fileLength = fs.Length;

        // DOS header: 'M','Z' then e_lfanew at 0x3C pointing to the PE signature.
        if (fileLength < DosLfanewOffset + 4)
            return;
        if (ReadU16(fs, 0) != 0x5A4D) // 'MZ'
            return;

        var peOffset = ReadU32(fs, DosLfanewOffset);
        if (peOffset + PeSignatureSize + CoffHeaderSize > fileLength)
            return;
        if (ReadU32(fs, peOffset) != 0x00004550) // 'P','E',0,0 (little-endian)
            return;

        var optHeaderOffset = peOffset + PeSignatureSize + CoffHeaderSize;
        if (optHeaderOffset + 2 > fileLength)
            return;

        // Optional header Magic: 0x10B = PE32, 0x20B = PE32+. The layout up to CheckSum is identical;
        // only the offsets of NumberOfRvaAndSizes / the data directory differ between the two.
        var optMagic = ReadU16(fs, optHeaderOffset);
        int numberOfRvaAndSizesOffset;
        int dataDirectoryOffset;
        switch (optMagic)
        {
            case 0x10B: // PE32
                numberOfRvaAndSizesOffset = 92;
                dataDirectoryOffset = 96;
                break;
            case 0x20B: // PE32+
                numberOfRvaAndSizesOffset = 108;
                dataDirectoryOffset = 112;
                break;
            default:
                return;
        }

        if (optHeaderOffset + numberOfRvaAndSizesOffset + 4 > fileLength)
            return;

        var numberOfRvaAndSizes = ReadU32(fs, optHeaderOffset + numberOfRvaAndSizesOffset);
        if (numberOfRvaAndSizes <= SecurityDirectoryIndex)
            return; // No Security data directory present — stub is not signed.

        var securityEntryOffset =
            optHeaderOffset + dataDirectoryOffset + SecurityDirectoryIndex * DataDirectoryEntrySize;
        if (securityEntryOffset + DataDirectoryEntrySize > fileLength)
            return;

        var certTableOffset = ReadU32(fs, securityEntryOffset);
        var certTableSize = ReadU32(fs, securityEntryOffset + 4);
        if (certTableOffset == 0 || certTableSize == 0)
            return; // Unsigned stub.

        // The signtool-produced signature places the cert table exactly at the stub's EOF. Only
        // preserve when that holds — otherwise extending the region would swallow non-cert bytes
        // that WERE part of the signed digest and break the signature. Skip (leave unsigned) instead.
        if ((long)certTableOffset + certTableSize != signedStubSize)
            return;

        // The new region must reach the actual EOF, and the values are PE DWORDs (uint). A bundle
        // large enough to overflow a uint cert region cannot use this technique; skip rather than
        // silently wrap (the payloads still self-extract; only the Authenticode signature is lost).
        var newCertRegionLength = fileLength - certTableOffset;
        if (newCertRegionLength > uint.MaxValue)
            return;

        // The attribute-certificate table must be a multiple of 8 bytes for WinVerifyTrust to
        // exclude it cleanly from the signed digest. Reattach pads the file to an 8-byte boundary
        // and cert tables start 8-aligned, so this holds — but if some exotic signed stub violates
        // it, skip rather than emit a table WinVerifyTrust would read as a bad digest (leaving the
        // bundle unsigned is honest; a corrupt-signature file is not).
        if (newCertRegionLength % 8 != 0)
            return;

        // Walk the WIN_CERTIFICATE entries within the ORIGINAL cert region to find the last one; only
        // it is grown, so any earlier certificates (e.g. a dual signature) stay intact.
        long lastCertOffset = certTableOffset;
        var pos = (long)certTableOffset;
        var regionEnd = (long)certTableOffset + certTableSize;
        while (pos + 8 <= regionEnd)
        {
            var dwLength = ReadU32(fs, pos);
            if (dwLength < 8)
                return; // Malformed cert table — do not touch a signature we cannot parse.
            lastCertOffset = pos;
            pos += (dwLength + 7) & ~7; // 8-byte aligned stride to the next WIN_CERTIFICATE
        }

        var newLastCertLength = fileLength - lastCertOffset;
        if (newLastCertLength > uint.MaxValue)
            return;

        // Patch order is irrelevant — both fields lie in Authenticode-excluded regions:
        //   * the last WIN_CERTIFICATE.dwLength (inside the cert region), and
        //   * the Security data-directory Size (the 8-byte directory entry, also skipped by the hash).
        WriteU32(fs, lastCertOffset, (uint)newLastCertLength);
        WriteU32(fs, securityEntryOffset + 4, (uint)newCertRegionLength);
    }

    private static ushort ReadU16(Stream stream, long offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private static uint ReadU32(Stream stream, long offset)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static void WriteU32(Stream stream, long offset, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(buffer);
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

            var magicBytes = BundleReader.BundleMagic;

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