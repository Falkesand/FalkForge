using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using FalkForge.Compiler.Msi;

namespace FalkForge.Decompiler;

/// <summary>
/// Reads WiX Burn bundle EXE files by parsing PE headers to locate the
/// <c>.wixburn</c> section, then extracting the UX container cabinet
/// to retrieve the Burn manifest XML.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WixBurnAccess : IWixBurnAccess
{
    private const uint WixBurnMagic = 0x00F14300;
    private const string ManifestFileKey = "0";

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly Guid _bundleId;
    private readonly uint _stubSize;
    private readonly uint _uxContainerSize;

    private WixBurnAccess(FileStream stream, BinaryReader reader, Guid bundleId, uint stubSize, uint uxContainerSize)
    {
        _stream = stream;
        _reader = reader;
        _bundleId = bundleId;
        _stubSize = stubSize;
        _uxContainerSize = uxContainerSize;
    }

    public Guid BundleId => _bundleId;

    public static Result<IWixBurnAccess> Open(string bundlePath)
    {
        if (!File.Exists(bundlePath))
            return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, $"WBD001: Bundle file not found: {bundlePath}");

        FileStream? stream = null;
        BinaryReader? reader = null;
        try
        {
            stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            // ── Parse PE headers ────────────────────────────────────────
            if (stream.Length < 64)
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, "WBD002: File is too small to be a valid PE executable.");
            }

            // Read e_lfanew at offset 0x3C
            stream.Seek(0x3C, SeekOrigin.Begin);
            var eLfanew = reader.ReadInt32();

            if (eLfanew < 0 || eLfanew + 4 > stream.Length)
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, "WBD002: Invalid PE header: e_lfanew out of range.");
            }

            // Verify PE signature "PE\0\0"
            stream.Seek(eLfanew, SeekOrigin.Begin);
            var peSignature = reader.ReadUInt32();
            if (peSignature != 0x00004550) // "PE\0\0" little-endian
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, "WBD002: Invalid PE signature.");
            }

            // COFF header starts at eLfanew + 4, 20 bytes total
            // NumberOfSections at offset 2, SizeOfOptionalHeader at offset 16
            var coffHeaderOffset = eLfanew + 4;
            stream.Seek(coffHeaderOffset + 2, SeekOrigin.Begin);
            var numberOfSections = reader.ReadUInt16();

            stream.Seek(coffHeaderOffset + 16, SeekOrigin.Begin);
            var sizeOfOptionalHeader = reader.ReadUInt16();

            // Section table starts after COFF header (20 bytes) + optional header
            var sectionTableOffset = coffHeaderOffset + 20 + sizeOfOptionalHeader;

            // ── Scan for .wixburn section ───────────────────────────────
            ReadOnlySpan<byte> wixburnName = [0x2E, 0x77, 0x69, 0x78, 0x62, 0x75, 0x72, 0x6E]; // ".wixburn"
            uint sectionRawDataOffset = 0;
            var foundSection = false;

            for (var i = 0; i < numberOfSections; i++)
            {
                var sectionOffset = sectionTableOffset + (i * 40);
                stream.Seek(sectionOffset, SeekOrigin.Begin);
                var sectionName = reader.ReadBytes(8);

                if (sectionName.AsSpan().SequenceEqual(wixburnName))
                {
                    // Skip VirtualSize (4) and VirtualAddress (4), read SizeOfRawData (4) and PointerToRawData (4)
                    reader.ReadUInt32(); // VirtualSize
                    reader.ReadUInt32(); // VirtualAddress
                    reader.ReadUInt32(); // SizeOfRawData
                    sectionRawDataOffset = reader.ReadUInt32(); // PointerToRawData
                    foundSection = true;
                    break;
                }
            }

            if (!foundSection)
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, "WBD003: PE file does not contain a .wixburn section.");
            }

            // ── Read .wixburn section data ──────────────────────────────
            stream.Seek(sectionRawDataOffset, SeekOrigin.Begin);

            var dwMagic = reader.ReadUInt32();
            if (dwMagic != WixBurnMagic)
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, $"WBD004: Invalid .wixburn magic: expected 0x{WixBurnMagic:X8}, found 0x{dwMagic:X8}.");
            }

            reader.ReadUInt32(); // dwVersion
            var guidBytes = reader.ReadBytes(16);
            var bundleId = new Guid(guidBytes);
            var stubSize = reader.ReadUInt32(); // dwStubSize
            reader.ReadUInt32(); // dwOriginalChecksum
            reader.ReadUInt32(); // dwOriginalSignatureOffset
            reader.ReadUInt32(); // dwOriginalSignatureSize
            reader.ReadUInt32(); // dwContainerFormat
            var containerCount = reader.ReadUInt32(); // dwContainerCount

            if (containerCount == 0)
            {
                reader.Dispose();
                stream.Dispose();
                return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, "WBD004: Bundle contains no containers.");
            }

            // Container sizes are a DWORD[] array — one uint32 per container
            var uxContainerSize = reader.ReadUInt32(); // rgcbContainers[0] = UX container

            stream.Seek(0, SeekOrigin.Begin);
            return Result<IWixBurnAccess>.Success(new WixBurnAccess(stream, reader, bundleId, stubSize, uxContainerSize));
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            stream?.Dispose();
            return Result<IWixBurnAccess>.Failure(ErrorKind.BundleError, $"WBD002: Failed to open WiX Burn bundle: {ex.Message}");
        }
    }

    public Result<XDocument> ReadManifest()
    {
        try
        {
            // UX container starts immediately after the stub
            _stream.Seek(_stubSize, SeekOrigin.Begin);

            const uint MaxUxContainerSize = 256 * 1024 * 1024; // 256 MB
            if (_uxContainerSize > MaxUxContainerSize)
                return Result<XDocument>.Failure(ErrorKind.BundleError, "WBD005: UX container size exceeds maximum allowed.");

            var containerBytes = new byte[_uxContainerSize];
            var totalRead = 0;
            while (totalRead < (int)_uxContainerSize)
            {
                var bytesRead = _stream.Read(containerBytes, totalRead, (int)_uxContainerSize - totalRead);
                if (bytesRead == 0)
                    return Result<XDocument>.Failure(ErrorKind.BundleError, "WBD005: Unexpected end of stream reading UX container.");
                totalRead += bytesRead;
            }

            using var cabinetStream = new MemoryStream(containerBytes, writable: false);
            var extractResult = CabinetExtractor.Extract(cabinetStream);
            if (extractResult.IsFailure)
                return Result<XDocument>.Failure(ErrorKind.BundleError, $"WBD005: Failed to extract UX container cabinet: {extractResult.Error.Message}");

            var files = extractResult.Value;
            if (!files.TryGetValue(ManifestFileKey, out var manifestBytes))
                return Result<XDocument>.Failure(ErrorKind.BundleError, "WBD006: Manifest file not found in UX container cabinet.");

            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            var document = XDocument.Load(manifestStream);
            return Result<XDocument>.Success(document);
        }
        catch (Exception ex)
        {
            return Result<XDocument>.Failure(ErrorKind.BundleError, $"WBD005: Failed to read Burn manifest: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
