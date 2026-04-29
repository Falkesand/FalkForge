using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class StandardLayoutsTests
{
    [Fact]
    public void Standard_layout_has_five_regions()
    {
        var layout = Layouts.Standard370x270;

        Assert.Equal(5, layout.Regions.Length);
        Assert.True(layout.RegionIndex.ContainsKey("Banner"));
        Assert.True(layout.RegionIndex.ContainsKey("TitleRow"));
        Assert.True(layout.RegionIndex.ContainsKey("ContentArea"));
        Assert.True(layout.RegionIndex.ContainsKey("BottomLine"));
        Assert.True(layout.RegionIndex.ContainsKey("ButtonRow"));
    }

    [Fact]
    public void Banner_bounds_match_legacy()
    {
        var layout = Layouts.Standard370x270;

        Assert.True(layout.TryGetRegion("Banner", out var banner));
        Assert.Equal(0, banner.Bounds.X);
        Assert.Equal(0, banner.Bounds.Y);
        Assert.Equal(370, banner.Bounds.Width);
        Assert.Equal(58, banner.Bounds.Height);
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
