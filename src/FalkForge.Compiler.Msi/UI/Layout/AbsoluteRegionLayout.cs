using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// <see cref="IRegionLayoutPolicy"/> for <see cref="RegionPolicy.Absolute"/> regions.
/// </summary>
/// <remarks>
/// Each <see cref="PlacedControl"/> supplies its own coordinates via <see cref="PlacedControl.OverrideX"/>,
/// <see cref="PlacedControl.OverrideY"/>, <see cref="PlacedControl.OverrideWidth"/>, and
/// <see cref="PlacedControl.OverrideHeight"/>. When an override is null, the policy falls back to
/// the region's origin (X/Y) and the per-region <see cref="RegionDefaults.ChildWidth"/> /
/// <see cref="RegionDefaults.ChildHeight"/> for the missing axis. This mirrors the legacy
/// hand-coordinate model used by SharedDialogBuilders templates.
/// </remarks>
internal sealed class AbsoluteRegionLayout : IRegionLayoutPolicy
{
    public ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls)
    {
        if (controls.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedControlPlacement>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ResolvedControlPlacement>(controls.Length);
        foreach (var control in controls)
        {
            var bounds = new Rect
            {
                X = control.OverrideX ?? region.Bounds.X,
                Y = control.OverrideY ?? region.Bounds.Y,
                Width = control.OverrideWidth ?? region.Defaults.ChildWidth,
                Height = control.OverrideHeight ?? region.Defaults.ChildHeight,
            };

            builder.Add(new ResolvedControlPlacement(control, bounds));
        }

        return builder.MoveToImmutable();
    }
}
