using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// <see cref="IRegionLayoutPolicy"/> for <see cref="RegionPolicy.RightPacked"/> regions.
/// </summary>
/// <remarks>
/// Children flow right-to-left starting at the region's right edge. The first control is the
/// rightmost. Each subsequent control is placed to the left of the previous, separated by
/// a gap pulled from <see cref="RegionDefaults.Gaps"/> when supplied (per-child override) or
/// <see cref="RegionDefaults.Gap"/> as the default. Per-control <see cref="PlacedControl.OverrideWidth"/>
/// and <see cref="PlacedControl.OverrideHeight"/> are honored; X and Y are computed by the policy
/// and override values for X/Y are ignored to keep the row packed.
/// </remarks>
internal sealed class RightPackedRegionLayout : IRegionLayoutPolicy
{
    public ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls)
    {
        if (controls.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedControlPlacement>.Empty;
        }

        var defaults = region.Defaults;
        var rightEdge = region.Bounds.X + region.Bounds.Width;
        var y = region.Bounds.Y;
        var defaultHeight = defaults.ChildHeight;
        var gaps = defaults.Gaps;

        var builder = ImmutableArray.CreateBuilder<ResolvedControlPlacement>(controls.Length);
        var cursor = rightEdge;

        for (var i = 0; i < controls.Length; i++)
        {
            var control = controls[i];
            var width = control.OverrideWidth ?? defaults.ChildWidth;
            var height = control.OverrideHeight ?? defaultHeight;

            if (i > 0)
            {
                // Gaps[i-1] supplies the gap before child[i] when present (legacy non-uniform spacing).
                var gapIndex = i - 1;
                var gap = gapIndex < gaps.Length ? gaps[gapIndex] : defaults.Gap;
                cursor -= gap;
            }

            cursor -= width;

            var bounds = new Rect
            {
                X = cursor,
                Y = y,
                Width = width,
                Height = height,
            };

            builder.Add(new ResolvedControlPlacement(control, bounds));
        }

        return builder.MoveToImmutable();
    }
}
