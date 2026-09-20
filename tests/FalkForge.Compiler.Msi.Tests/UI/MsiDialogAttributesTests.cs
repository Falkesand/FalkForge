using FalkForge.Compiler.Msi.UI;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

public sealed class MsiDialogAttributesTests
{
    // Windows Installer Dialog Style Bits:
    // https://learn.microsoft.com/en-us/windows/win32/msi/dialog-style-bits
    [Fact]
    public void Values_match_the_Windows_Installer_contract()
    {
        Assert.Equal(0, (int)MsiDialogAttributes.None);
        Assert.Equal(1, (int)MsiDialogAttributes.Visible);
        Assert.Equal(2, (int)MsiDialogAttributes.Modal);
        Assert.Equal(4, (int)MsiDialogAttributes.Minimize);
        Assert.Equal(8, (int)MsiDialogAttributes.SysModal);
        Assert.Equal(16, (int)MsiDialogAttributes.KeepModeless);
        Assert.Equal(32, (int)MsiDialogAttributes.TrackDiskSpace);
        Assert.Equal(64, (int)MsiDialogAttributes.UseCustomPalette);
        Assert.Equal(128, (int)MsiDialogAttributes.RightToLeftReadingOrder);
        Assert.Equal(256, (int)MsiDialogAttributes.RightAligned);
        Assert.Equal(512, (int)MsiDialogAttributes.LeftScroll);
        Assert.Equal(896, (int)MsiDialogAttributes.BiDi);
        Assert.Equal(65536, (int)MsiDialogAttributes.Error);
    }
}
