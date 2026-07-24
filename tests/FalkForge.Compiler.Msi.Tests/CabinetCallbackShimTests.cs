using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
///     Pins the C-style open-flag mapping shared by <see cref="Msi.CabinetBuilder"/> (FCI) and
///     <see cref="Msi.CabinetExtractor"/> (FDI) via <see cref="CabinetCallbackShim"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CabinetCallbackShimTests
{
    [Theory]
    // _O_RDONLY (0x0000): open an existing file for reading only.
    [InlineData(0x0000, FileMode.Open, FileAccess.Read)]
    // _O_WRONLY | _O_CREAT | _O_TRUNC (0x0001 | 0x0100 | 0x0200): create-or-overwrite for writing.
    [InlineData(0x0001 | 0x0100 | 0x0200, FileMode.Create, FileAccess.Write)]
    // _O_RDWR (0x0002): open an existing file for read and write.
    [InlineData(0x0002, FileMode.Open, FileAccess.ReadWrite)]
    // _O_RDONLY | _O_BINARY (0x0000 | 0x8000): the binary flag carries no .NET FileMode/FileAccess
    // meaning (streams are always binary) and must be ignored by the mapping.
    [InlineData(0x0000 | 0x8000, FileMode.Open, FileAccess.Read)]
    // _O_WRONLY | _O_CREAT (0x0001 | 0x0100), no _O_TRUNC: create the file if it does not exist,
    // otherwise open the existing file without truncating it.
    [InlineData(0x0001 | 0x0100, FileMode.OpenOrCreate, FileAccess.Write)]
    // _O_RDWR | _O_TRUNC (0x0002 | 0x0200), no _O_CREAT: the file must already exist and is
    // truncated to zero length.
    [InlineData(0x0002 | 0x0200, FileMode.Truncate, FileAccess.ReadWrite)]
    public void MapOpenFlags_KnownOflagCombinations_MapToExpectedModeAndAccess(
        int oflag, FileMode expectedMode, FileAccess expectedAccess)
    {
        var (mode, access) = CabinetCallbackShim.MapOpenFlags(oflag);

        Assert.Equal(expectedMode, mode);
        Assert.Equal(expectedAccess, access);
    }
}
