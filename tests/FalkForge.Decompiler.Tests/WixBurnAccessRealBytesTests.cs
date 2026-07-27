using System.Runtime.Versioning;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Exercises the real <see cref="WixBurnAccess"/> PE parser against synthetic byte arrays
/// written to temp files. Unlike the tests in <c>DecompilerErrorCodeTests.cs</c> and
/// <c>WixBundleDecompilerTests.cs</c>, which drive <see cref="MockWixBurnAccess"/>, these tests
/// invoke <see cref="WixBurnAccess.Open"/> directly, so they verify the hand-written PE/Burn
/// section parser itself rather than a hardcoded stand-in. <c>WixBurnAccess</c> parses bytes
/// from a foreign (attacker-controlled) executable, so this file's job is to prove the parser
/// rejects malformed input safely and accepts well-formed input correctly.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WixBurnAccessRealBytesTests
{
    private const uint WixBurnMagic = 0x00F14300;

    // ── Synthetic PE/Burn byte-layout builder ───────────────────────────────────
    //
    // Layout produced by BuildBundleBytes (offsets are for the default eLfanew = 0x80):
    //
    //   0x00-0x3B  DOS header padding (zero)
    //   0x3C-0x3F  e_lfanew (Int32)                             -> default 0x80
    //   0x40-0x7F  DOS stub padding (zero)
    //   0x80-0x83  PE signature "PE\0\0"                        (uint 0x00004550)
    //   0x84-0x97  COFF header (20 bytes): Machine, NumberOfSections,
    //              TimeDateStamp, PointerToSymbolTable, NumberOfSymbols,
    //              SizeOfOptionalHeader, Characteristics
    //   ...        Optional header (SizeOfOptionalHeader bytes, zero-filled)
    //   ...        Section table: NumberOfSections * 40-byte entries
    //              (Name[8], VirtualSize, VirtualAddress, SizeOfRawData,
    //               PointerToRawData, PointerToRelocations, PointerToLinenumbers,
    //               NumberOfRelocations, NumberOfLinenumbers, Characteristics)
    //   ...        .wixburn raw section data (52 bytes), when included:
    //              dwMagic, dwVersion, bundleId (16-byte GUID), dwStubSize,
    //              dwOriginalChecksum, dwOriginalSignatureOffset,
    //              dwOriginalSignatureSize, dwContainerFormat, dwContainerCount,
    //              rgcbContainers[0] (UX container size)
    private static byte[] BuildBundleBytes(
        bool includeWixburnSection = true,
        uint wixburnMagic = WixBurnMagic,
        uint containerCount = 1,
        uint uxContainerSize = 16,
        uint stubSize = 100,
        Guid? bundleId = null,
        ushort numberOfSections = 1,
        int eLfanew = 0x80)
    {
        var buffer = new List<byte>();

        // DOS header up to e_lfanew at offset 0x3C.
        buffer.AddRange(new byte[0x3C]);
        buffer.AddRange(BitConverter.GetBytes(eLfanew));
        while (buffer.Count < eLfanew)
            buffer.Add(0);

        // PE signature "PE\0\0".
        buffer.AddRange(BitConverter.GetBytes(0x00004550u));

        // COFF header (20 bytes). SizeOfOptionalHeader is 0 - the parser only
        // uses it to skip past the optional header, it never reads its contents.
        buffer.AddRange(BitConverter.GetBytes((ushort)0));       // Machine
        buffer.AddRange(BitConverter.GetBytes(numberOfSections)); // NumberOfSections
        buffer.AddRange(BitConverter.GetBytes(0u));               // TimeDateStamp
        buffer.AddRange(BitConverter.GetBytes(0u));               // PointerToSymbolTable
        buffer.AddRange(BitConverter.GetBytes(0u));               // NumberOfSymbols
        buffer.AddRange(BitConverter.GetBytes((ushort)0));        // SizeOfOptionalHeader
        buffer.AddRange(BitConverter.GetBytes((ushort)0));        // Characteristics

        var wixburnPointerToRawDataIndex = -1;

        for (var i = 0; i < numberOfSections; i++)
        {
            var isTarget = includeWixburnSection && i == numberOfSections - 1;
            var name = isTarget
                ? ".wixburn"u8.ToArray()
                : ".text\0\0\0"u8.ToArray();

            buffer.AddRange(name);                       // Name (8 bytes)
            buffer.AddRange(BitConverter.GetBytes(0u));   // VirtualSize
            buffer.AddRange(BitConverter.GetBytes(0u));   // VirtualAddress
            buffer.AddRange(BitConverter.GetBytes(0u));   // SizeOfRawData

            if (isTarget)
                wixburnPointerToRawDataIndex = buffer.Count;
            buffer.AddRange(BitConverter.GetBytes(0u));   // PointerToRawData (placeholder)

            // PointerToRelocations(4) + PointerToLinenumbers(4) + NumberOfRelocations(2)
            // + NumberOfLinenumbers(2) + Characteristics(4) = 16 bytes, all unused by the parser.
            buffer.AddRange(new byte[16]);
        }

        var payloadOffset = buffer.Count;
        if (wixburnPointerToRawDataIndex >= 0)
        {
            var offsetBytes = BitConverter.GetBytes((uint)payloadOffset);
            for (var k = 0; k < 4; k++)
                buffer[wixburnPointerToRawDataIndex + k] = offsetBytes[k];
        }

        if (includeWixburnSection)
        {
            buffer.AddRange(BitConverter.GetBytes(wixburnMagic));
            buffer.AddRange(BitConverter.GetBytes(1u));                       // dwVersion
            buffer.AddRange((bundleId ?? Guid.NewGuid()).ToByteArray());      // 16-byte GUID
            buffer.AddRange(BitConverter.GetBytes(stubSize));                 // dwStubSize
            buffer.AddRange(BitConverter.GetBytes(0u));                       // dwOriginalChecksum
            buffer.AddRange(BitConverter.GetBytes(0u));                       // dwOriginalSignatureOffset
            buffer.AddRange(BitConverter.GetBytes(0u));                       // dwOriginalSignatureSize
            buffer.AddRange(BitConverter.GetBytes(0u));                       // dwContainerFormat
            buffer.AddRange(BitConverter.GetBytes(containerCount));           // dwContainerCount
            buffer.AddRange(BitConverter.GetBytes(uxContainerSize));          // rgcbContainers[0]
        }

        return buffer.ToArray();
    }

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wixburn-realbytes-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ── Truncated / too-short file ───────────────────────────────────────────────

    [Fact]
    public void Open_FileShorterThan64Bytes_ReturnsWbd002()
    {
        var path = WriteTempFile(new byte[32]);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD002", result.Error.Message);
            Assert.Contains("too small", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_FileDoesNotExist_ReturnsWbd001()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wixburn-missing-{Guid.NewGuid():N}.exe");

        var result = WixBurnAccess.Open(path);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
        Assert.Contains("WBD001", result.Error.Message);
    }

    // ── Bad / out-of-range e_lfanew ──────────────────────────────────────────────

    [Fact]
    public void Open_NegativeELfanew_ReturnsWbd002OutOfRange()
    {
        // 64-byte file (passes the length gate) with e_lfanew = -1 at offset 0x3C.
        var bytes = new byte[64];
        BitConverter.GetBytes(-1).CopyTo(bytes, 0x3C);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD002", result.Error.Message);
            Assert.Contains("e_lfanew out of range", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The explicit "e_lfanew out of range" guard must reject an e_lfanew near
    // Int32.MaxValue on its own — without relying on the downstream Seek/Read throwing
    // EndOfStreamException. Before the fix, the guard was `eLfanew < 0 || eLfanew + 4 >
    // stream.Length` computed in unchecked Int32 arithmetic (the project does not set
    // <CheckForOverflowUnderflow>): with e_lfanew = Int32.MaxValue - 3, `eLfanew + 4`
    // silently wrapped to Int32.MinValue, so the second half of the check was always
    // false and the out-of-range guard never fired. The failure only happened to be
    // caught by the generic open-failure catch instead. Now the comparison widens to
    // long before adding, so it cannot overflow and the guard rejects directly.
    [Fact]
    public void Open_ELfanewIntegerOverflow_IsRejectedByExplicitRangeCheck()
    {
        var bytes = new byte[64];
        BitConverter.GetBytes(int.MaxValue - 3).CopyTo(bytes, 0x3C); // eLfanew + 4 would overflow Int32 if computed unchecked
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD002", result.Error.Message);
            // The explicit "out of range" guard must be the one that rejects this,
            // not the generic open-failure catch.
            Assert.Contains("out of range", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Bad DOS/PE signature ─────────────────────────────────────────────────────

    [Fact]
    public void Open_BadPeSignature_ReturnsWbd002()
    {
        var bytes = BuildBundleBytes();
        BitConverter.GetBytes(0xBAADF00Du).CopyTo(bytes, 0x80); // corrupt "PE\0\0" at eLfanew
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD002", result.Error.Message);
            Assert.Contains("Invalid PE signature", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── No .wixburn section ──────────────────────────────────────────────────────

    [Fact]
    public void Open_SectionTableHasNoWixburnEntry_ReturnsWbd003()
    {
        // One real section (".text") is present so the scan loop actually walks an
        // entry and rejects it by name, rather than short-circuiting on zero sections.
        var bytes = BuildBundleBytes(includeWixburnSection: false, numberOfSections: 1);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD003", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Bad .wixburn magic ───────────────────────────────────────────────────────

    [Fact]
    public void Open_WixburnSectionHasWrongMagic_ReturnsWbd004()
    {
        var bytes = BuildBundleBytes(wixburnMagic: 0xDEADBEEF);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD004", result.Error.Message);
            Assert.Contains("Invalid .wixburn magic", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Zero containers (DoS-guard adjacent: rejects an empty container list) ───

    [Fact]
    public void Open_ZeroContainerCount_ReturnsWbd004()
    {
        var bytes = BuildBundleBytes(containerCount: 0);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.BundleError, result.Error.Kind);
            Assert.Contains("WBD004", result.Error.Message);
            Assert.Contains("no containers", result.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── UX container size DoS guard (ReadManifest, WBD005) ──────────────────────

    [Fact]
    public void ReadManifest_UxContainerSizeExceeds256Mb_ReturnsWbd005WithoutAllocating()
    {
        // uxContainerSize is attacker-controlled (read straight from the .wixburn
        // section). If ReadManifest allocated `new byte[_uxContainerSize]` before
        // checking the bound, a hostile bundle could force a multi-gigabyte
        // allocation per parse attempt. Assert the 256 MB guard rejects it first.
        var bytes = BuildBundleBytes(uxContainerSize: 400 * 1024 * 1024);
        var path = WriteTempFile(bytes);
        try
        {
            var openResult = WixBurnAccess.Open(path);
            Assert.True(openResult.IsSuccess);
            using var access = openResult.Value;

            var manifestResult = access.ReadManifest();

            Assert.True(manifestResult.IsFailure);
            Assert.Equal(ErrorKind.BundleError, manifestResult.Error.Kind);
            Assert.Contains("WBD005", manifestResult.Error.Message);
            Assert.Contains("exceeds maximum", manifestResult.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadManifest_TruncatedUxContainer_ReturnsWbd005UnexpectedEndOfStream()
    {
        // uxContainerSize claims more bytes than the file actually has after the stub.
        var bytes = BuildBundleBytes(stubSize: 0, uxContainerSize: 1024);
        var path = WriteTempFile(bytes);
        try
        {
            var openResult = WixBurnAccess.Open(path);
            Assert.True(openResult.IsSuccess);
            using var access = openResult.Value;

            var manifestResult = access.ReadManifest();

            Assert.True(manifestResult.IsFailure);
            Assert.Equal(ErrorKind.BundleError, manifestResult.Error.Kind);
            Assert.Contains("WBD005", manifestResult.Error.Message);
            Assert.Contains("Unexpected end of stream", manifestResult.Error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Well-formed minimal case: the parser must also say yes ──────────────────

    [Fact]
    public void Open_WellFormedMinimalBundle_ParsesSuccessfullyAndExposesBundleId()
    {
        var expectedId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var bytes = BuildBundleBytes(bundleId: expectedId, stubSize: 200, containerCount: 1, uxContainerSize: 16);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsSuccess);
            using var access = result.Value;
            Assert.Equal(expectedId, access.BundleId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_WellFormedBundleWithMultipleSections_FindsWixburnAmongOthers()
    {
        // Two sections: a leading ".text"-style entry followed by ".wixburn". Proves the
        // scan loop correctly walks past a non-matching entry instead of only working
        // when .wixburn happens to be section zero.
        var bytes = BuildBundleBytes(numberOfSections: 2, includeWixburnSection: true);
        var path = WriteTempFile(bytes);
        try
        {
            var result = WixBurnAccess.Open(path);

            Assert.True(result.IsSuccess);
            using var access = result.Value;
            Assert.NotEqual(Guid.Empty, access.BundleId);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
