using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Stock <see cref="DialogLayout"/> instances used by the dialog composer.
/// </summary>
public static class Layouts
{
    /// <summary>
    /// Standard MSI dialog layout: 370x270 Installer Unit canvas, six named regions matching
    /// the hand-coded geometry baked into the legacy SharedDialogBuilders templates. Used
    /// by every stock template until per-template overrides become useful.
    /// </summary>
    public static readonly DialogLayout Standard370x270 = new()
    {
        Name = "Standard370x270",
        CanvasWidth = 370,
        CanvasHeight = 270,
        Regions = ImmutableArray.Create(
            new DialogRegion
            {
                Name = "Banner",
                // 44 Installer Units matches WiX's own banner convention (InstallDirDlg.wxs
                // BannerBitmap/BannerLine height); 58 is the classic *pixel* height for that
                // same banner, not the Installer Unit value.
                Bounds = new Rect { X = 0, Y = 0, Width = 370, Height = 44 },
                Policy = RegionPolicy.SingleControl,
            },
            new DialogRegion
            {
                Name = "TitleRow",
                Bounds = new Rect { X = 15, Y = 6, Width = 200, Height = 15 },
                Policy = RegionPolicy.Absolute,
            },
            new DialogRegion
            {
                Name = "BannerLine",
                // Y=44 matches the Banner region's own bottom edge (Y=0, Height=44), the same
                // WiX InstallDirDlg.wxs places its BannerLine at. Width is deliberately set to
                // 370, not WiX's 373, to stay internally consistent with this layout's own
                // BottomLine convention below (see the beta.7 release notes).
                Bounds = new Rect { X = 0, Y = 44, Width = 370, Height = 0 },
                Policy = RegionPolicy.SingleControl,
            },
            new DialogRegion
            {
                Name = "ContentArea",
                Bounds = new Rect { X = 15, Y = 60, Width = 340, Height = 165 },
                Policy = RegionPolicy.Absolute,
            },
            new DialogRegion
            {
                Name = "BottomLine",
                Bounds = new Rect { X = 0, Y = 234, Width = 370, Height = 0 },
                Policy = RegionPolicy.SingleControl,
            },
            new DialogRegion
            {
                Name = "ButtonRow",
                Bounds = new Rect { X = 0, Y = 243, Width = 360, Height = 17 },
                Policy = RegionPolicy.RightPacked,
                Defaults = new RegionDefaults { ChildWidth = 56, ChildHeight = 17, Gap = 8 },
            }),
    };
}
