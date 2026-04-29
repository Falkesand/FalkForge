using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// <see cref="IRegionLayoutPolicy"/> for <see cref="RegionPolicy.TopStacked"/> regions.
/// </summary>
/// <remarks>
/// Children flow top-to-bottom from the region's top edge. Each child's Y is the previous
/// child's bottom edge plus <see cref="RegionDefaults.Gap"/>; per-control
/// <see cref="PlacedControl.OverrideHeight"/> propagates into the next child's Y so that
/// stacks of mixed-height items remain non-overlapping. X defaults to the region's X and
/// Width defaults to the region's width unless overridden per-control.
/// </remarks>
internal sealed class TopStackedRegionLayout : IRegionLayoutPolicy
{
    public ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls)
    {
        if (controls.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedControlPlacement>.Empty;
        }

        var defaults = region.Defaults;
        var x = region.Bounds.X;
        var defaultWidth = region.Bounds.Width;
        var defaultHeight = defaults.ChildHeight;
        var gap = defaults.Gap;

        var builder = ImmutableArray.CreateBuilder<ResolvedControlPlacement>(controls.Length);
        var cursorY = region.Bounds.Y;

        for (var i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            var width = control.OverrideWidth ?? defaultWidth;
            var height = control.OverrideHeight ?? defaultHeight;

            var bounds = new Rect
            {
                X = x,
                Y = cursorY,
                Width = width,
                Height = height,
            };

            builder.Add(new ResolvedControlPlacement(control, bounds));
            cursorY += height + gap;
        }

        return builder.MoveToImmutable();
    }
}
