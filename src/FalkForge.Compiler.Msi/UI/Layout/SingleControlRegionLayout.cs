using System;
using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// <see cref="IRegionLayoutPolicy"/> for <see cref="RegionPolicy.SingleControl"/> regions.
/// </summary>
/// <remarks>
/// The region holds at most one control whose bounds equal the region's <see cref="DialogRegion.Bounds"/>.
/// Zero controls is permitted (the region is reserved but unused). More than one control violates
/// the policy and raises an <see cref="InvalidOperationException"/>.
/// </remarks>
internal sealed class SingleControlRegionLayout : IRegionLayoutPolicy
{
    public ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls)
    {
        if (controls.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedControlPlacement>.Empty;
        }

        if (controls.Length > 1)
        {
            throw new InvalidOperationException(
                $"Region '{region.Name}' uses {nameof(RegionPolicy.SingleControl)} policy but received {controls.Length} controls; expected at most 1.");
        }

        return ImmutableArray.Create(new ResolvedControlPlacement(controls[0], region.Bounds));
    }
}
