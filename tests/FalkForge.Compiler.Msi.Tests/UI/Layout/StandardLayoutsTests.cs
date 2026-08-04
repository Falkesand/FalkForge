using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class StandardLayoutsTests
{
    [Fact]
    public void Standard_layout_has_six_regions()
    {
        var layout = Layouts.Standard370x270;

        Assert.Equal(6, layout.Regions.Length);
        Assert.True(layout.RegionIndex.ContainsKey("Banner"));
        Assert.True(layout.RegionIndex.ContainsKey("TitleRow"));
        Assert.True(layout.RegionIndex.ContainsKey("BannerLine"));
        Assert.True(layout.RegionIndex.ContainsKey("ContentArea"));
        Assert.True(layout.RegionIndex.ContainsKey("BottomLine"));
        Assert.True(layout.RegionIndex.ContainsKey("ButtonRow"));
    }

    [Fact]
    public void BannerLine_region_sits_at_the_Banner_regions_bottom_edge()
    {
        // WiX's InstallDirDlg.wxs places BannerLine at X=0, Y=44, Height=0 — the same Y as the
        // Banner region's own bottom edge (Y=0, Height=44). Width matches this repo's own
        // BottomLine convention (370, the canvas width) rather than WiX's 373, for internal
        // consistency with the sibling separator already in this layout.
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("Banner", out var banner));
        Assert.True(layout.TryGetRegion("BannerLine", out var bannerLine));

        Assert.Equal(0, bannerLine.Bounds.X);
        Assert.Equal(banner.Bounds.Y + banner.Bounds.Height, bannerLine.Bounds.Y);
        Assert.Equal(44, bannerLine.Bounds.Y);
        Assert.Equal(370, bannerLine.Bounds.Width);
        Assert.Equal(0, bannerLine.Bounds.Height);
        Assert.Equal(RegionPolicy.SingleControl, bannerLine.Policy);
    }

    [Fact]
    public void Banner_bounds_match_WiX()
    {
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("Banner", out var banner));
        Assert.Equal(0, banner.Bounds.X);
        Assert.Equal(0, banner.Bounds.Y);
        Assert.Equal(370, banner.Bounds.Width);
        // 44 Installer Units matches WiX's own BannerBitmap/BannerLine convention
        // (WelcomeDlg.wxs Bitmap is 370x234, InstallDirDlg.wxs BannerBitmap is 370x44) — not
        // 58, which was the banner's prescribed *pixel* height mistakenly written into this
        // Installer Unit field.
        Assert.Equal(44, banner.Bounds.Height);
    }

    [Fact]
    public void Banner_region_bottom_edge_keeps_WiX_16_unit_gap_before_ContentArea()
    {
        // WiX's InstallDirDlg.wxs places its first content control (FolderLabel) at Y=60, and
        // its BannerLine separator at Y=44 (the Banner region's bottom edge). Pin that the
        // Banner region's height keeps the same 16-unit gap ahead of ContentArea's Y=60 origin,
        // rather than the two overlapping.
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("Banner", out var banner));
        Assert.True(layout.TryGetRegion("ContentArea", out var content));

        var bannerBottom = banner.Bounds.Y + banner.Bounds.Height;
        Assert.Equal(44, bannerBottom);
        Assert.Equal(60, content.Bounds.Y);
    }

    [Fact]
    public void ButtonRow_uses_right_packed_policy()
    {
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("ButtonRow", out var buttonRow));
        Assert.Equal(RegionPolicy.RightPacked, buttonRow.Policy);
        Assert.Equal(56, buttonRow.Defaults.ChildWidth);
        Assert.Equal(17, buttonRow.Defaults.ChildHeight);
        Assert.Equal(8, buttonRow.Defaults.Gap);
    }

    [Fact]
    public void ContentArea_uses_absolute_policy()
    {
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("ContentArea", out var content));
        Assert.Equal(RegionPolicy.Absolute, content.Policy);
        Assert.Equal(15, content.Bounds.X);
        Assert.Equal(60, content.Bounds.Y);
        Assert.Equal(340, content.Bounds.Width);
        Assert.Equal(165, content.Bounds.Height);
    }

    [Fact]
    public void BottomLine_height_zero_acts_as_separator_marker()
    {
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("BottomLine", out var bottom));
        Assert.Equal(0, bottom.Bounds.Height);
        Assert.Equal(RegionPolicy.SingleControl, bottom.Policy);
        Assert.Equal(370, bottom.Bounds.Width);
        Assert.Equal(234, bottom.Bounds.Y);
    }

    [Fact]
    public void Layout_canvas_is_370_by_270()
    {
        var layout = Layouts.Standard370x270;

        Assert.Equal("Standard370x270", layout.Name);
        Assert.Equal(370, layout.CanvasWidth);
        Assert.Equal(270, layout.CanvasHeight);
    }
}
