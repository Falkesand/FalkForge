using System.Runtime.Versioning;
using System.Text;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
internal static class SummaryInfoPatcher
{
    private const int CfbHeaderBytes = 512;
    private const int OffSectorShift = 0x1E; // uint16: log2(sectorSize)
    private const int OffFirstDirSector = 0x30; // int32: first directory sector index
    private const int OffFatArray = 0x4C; // 109 int32 FAT sector indices
    private const int DirEntrySizeBytes = 128;
    private const int OffDirNameLen = 0x40; // uint16 name length (includes NUL)
    private const int OffDirEntryType = 0x42; // byte: 0=empty, 1=storage, 2=stream, 5=root
    private const int OffDirCreatedTime = 0x64; // FILETIME (8 bytes)
    private const int OffDirModifiedTime = 0x6C; // FILETIME (8 bytes)
    private const int OffDirStartSector = 0x74; // int32 start sector of stream data
    private const int EndOfChain = -2; // 0xFFFFFFFE
    private const uint VtFiletime = 64;
    private const uint PidCreateDtm = 12;
    private const uint PidLastsaveDtm = 13;

    internal static Result<Unit> PatchTimestamps(string msiPath, DateTime normalizedTimestamp)
    {
        var ft = normalizedTimestamp.ToFileTimeUtc();
        var ftBytes = BitConverter.GetBytes(ft);
        var zeroTimestamp = new byte[8]; // directory entry timestamps -> zero for reproducibility
        try
        {
            using var fs = new FileStream(msiPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var rw = new BinaryReader(fs, Encoding.UTF8, true);
            if (fs.Length < CfbHeaderBytes)
                return Result<Unit>.Failure(ErrorKind.CompilationError,
                    "MSI file is too small to be a valid OLE2 compound file.");

            // Read sectorShift from OLE2 header (offset 0x1E); sectorSize = 1 << sectorShift
            // v3 = 512B (sectorShift=9), v4 = 4096B (sectorShift=12). Windows Installer creates v4.
            fs.Position = OffSectorShift;
            var sectorShift = rw.ReadUInt16();
            if (sectorShift < 1 || sectorShift > 14)
                return Unit.Value;
            var sectorSize = 1 << sectorShift;
            var entriesPerFatSector = sectorSize / 4;

            // Build FAT from DIFAT array in header (109 FAT sector indices at offset 0x4C)
            fs.Position = OffFatArray;
            var fatSectors = new List<int>();
            for (var i = 0; i < 109; i++)
            {
                var s = rw.ReadInt32();
                if (s < 0) break;
                fatSectors.Add(s);
            }

            var fat = new int[fatSectors.Count * entriesPerFatSector];
            for (var i = 0; i < fatSectors.Count; i++)
            {
                fs.Position = SectorOffset(fatSectors[i], sectorSize);
                for (var j = 0; j < entriesPerFatSector; j++)
                    fat[i * entriesPerFatSector + j] = rw.ReadInt32();
            }

            // Read first directory sector index
            fs.Position = OffFirstDirSector;
            var dirSector = rw.ReadInt32();

            // Walk all directory sectors:
            // 1. Zero out directory entry CreatedTime/ModifiedTime (non-deterministic on-disk metadata)
            // 2. Locate the \x05SummaryInformation stream
            var dirEntriesPerSector = sectorSize / DirEntrySizeBytes;
            (string? Name, int StartSector, int Size) summaryEntry = default;

            while (dirSector != EndOfChain && dirSector >= 0)
            {
                var offset = SectorOffset(dirSector, sectorSize);
                fs.Position = offset;
                var sectorData = rw.ReadBytes(sectorSize);
                var modified = false;

                for (var i = 0; i < dirEntriesPerSector; i++)
                {
                    var entryBase = i * DirEntrySizeBytes;

                    // Zero timestamps for ALL directory entries, including inactive (type=0) ones.
                    // Windows Installer stamps ModifiedTime on every entry it writes, even slots
                    // it later marks as free — skipping type=0 entries left stale FILETIMEs on disk.
                    Array.Copy(zeroTimestamp, 0, sectorData, entryBase + OffDirCreatedTime, 8);
                    Array.Copy(zeroTimestamp, 0, sectorData, entryBase + OffDirModifiedTime, 8);
                    modified = true;

                    // Name inspection only applies to active entries.
                    var entryType = sectorData[entryBase + OffDirEntryType];
                    if (entryType == 0) continue;
                    var nameLen = BitConverter.ToUInt16(sectorData, entryBase + OffDirNameLen);
                    if (nameLen < 2) continue;

                    // Identify the SummaryInformation stream
                    if (summaryEntry.Name is null)
                    {
                        var nameBytes = new byte[nameLen - 2];
                        Buffer.BlockCopy(sectorData, entryBase, nameBytes, 0, nameLen - 2);
                        var name = Encoding.Unicode.GetString(nameBytes);
                        if (name.Equals("\x05SummaryInformation", StringComparison.Ordinal))
                        {
                            var start = BitConverter.ToInt32(sectorData, entryBase + OffDirStartSector);
                            var size = BitConverter.ToInt32(sectorData, entryBase + OffDirStartSector + 4);
                            summaryEntry = (name, start, size);
                        }
                    }
                }

                if (modified)
                {
                    fs.Position = offset;
                    fs.Write(sectorData, 0, sectorSize);
                }

                dirSector = dirSector < fat.Length ? fat[dirSector] : EndOfChain;
            }

            // Patch PID_CREATE_DTM and PID_LASTSAVE_DTM inside the SummaryInformation property set
            if (summaryEntry.Name is not null)
            {
                var streamBytes = ReadSectorChain(fs, rw, fat, summaryEntry.StartSector, summaryEntry.Size, sectorSize);
                PatchPropertySetFiletimes(streamBytes, ftBytes);
                WriteSectorChain(fs, fat, summaryEntry.StartSector, streamBytes, sectorSize);
            }

            return Unit.Value;
        }
        catch (IOException ex)
        {
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to patch MSI SummaryInfo timestamps: {ex.Message}");
        }
    }

    /// <summary>
    ///     Per [MS-CFB]: sector N begins at byte offset (N+1)*sectorSize.
    ///     Sector 0 immediately follows the 512-byte header, regardless of sectorSize.
    /// </summary>
    private static long SectorOffset(int sector, int sectorSize)
    {
        return (long)(sector + 1) * sectorSize;
    }

    private static byte[] ReadSectorChain(FileStream fs, BinaryReader rw, int[] fat,
        int startSector, int size, int sectorSize)
    {
        var data = new byte[size];
        var written = 0;
        var sector = startSector;
        while (sector != EndOfChain && sector >= 0 && written < size)
        {
            fs.Position = SectorOffset(sector, sectorSize);
            var toRead = Math.Min(sectorSize, size - written);
            var read = fs.Read(data, written, toRead);
            written += read;
            sector = sector < fat.Length ? fat[sector] : EndOfChain;
        }

        return data;
    }

    private static void WriteSectorChain(FileStream fs, int[] fat,
        int startSector, byte[] data, int sectorSize)
    {
        var written = 0;
        var sector = startSector;
        while (sector != EndOfChain && sector >= 0 && written < data.Length)
        {
            fs.Position = SectorOffset(sector, sectorSize);
            var toWrite = Math.Min(sectorSize, data.Length - written);
            fs.Write(data, written, toWrite);
            written += toWrite;
            sector = sector < fat.Length ? fat[sector] : EndOfChain;
        }
    }

    private static void PatchPropertySetFiletimes(byte[] data, byte[] ftBytes)
    {
        // OLE2 property set header layout (little-endian):
        // 0x00: ByteOrder(2) + Version(2) + SystemID(4) + CLSID(16) + SectionCount(4) = 28 bytes
        // 0x1C: FMTID (16 bytes)
        // 0x2C: SectionOffset (int32) - byte offset from start of property set to section
        if (data.Length < 0x30) return;
        var sectionOffset = BitConverter.ToInt32(data, 0x2C);
        if (sectionOffset < 0 || sectionOffset + 8 > data.Length) return;

        // Section: Size(4) + PropertyCount(4) + PropertyIdentifierAndOffset[Count]
        var propCount = BitConverter.ToInt32(data, sectionOffset + 4);
        if (propCount <= 0) return;

        var idTableBase = sectionOffset + 8;
        for (var i = 0; i < propCount; i++)
        {
            var idEntryBase = idTableBase + i * 8;
            if (idEntryBase + 8 > data.Length) break;

            var propId = BitConverter.ToUInt32(data, idEntryBase);
            var propOffset = BitConverter.ToInt32(data, idEntryBase + 4);

            if (propId != PidCreateDtm && propId != PidLastsaveDtm) continue;

            // Property value: TypeCode(uint32=64 for VT_FILETIME) + FILETIME(8 bytes)
            var valueBase = sectionOffset + propOffset;
            if (valueBase + 12 > data.Length) continue;
            var typeCode = BitConverter.ToUInt32(data, valueBase);
            if (typeCode != VtFiletime) continue;

            Array.Copy(ftBytes, 0, data, valueBase + 4, 8);
        }
    }
}