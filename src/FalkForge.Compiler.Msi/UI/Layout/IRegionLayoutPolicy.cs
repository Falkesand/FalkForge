using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Resolves the absolute geometry of controls inside a <see cref="DialogRegion"/>.
/// </summary>
/// <remarks>
/// One implementation per <see cref="RegionPolicy"/> value. Implementations are pure:
/// they receive the region and the list of placed controls in input order and return
/// a parallel array of resolved bounds.
/// </remarks>
internal interface IRegionLayoutPolicy
{
    /// <summary>
    /// Computes absolute (X, Y, Width, Height) for each control in the region.
    /// Returns control entries in input order.
    /// </summary>
    ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls);
}

/// <summary>
/// Pairs a <see cref="PlacedControl"/> with its policy-resolved <see cref="Rect"/> bounds.
/// </summary>
internal sealed record ResolvedControlPlacement(PlacedControl Source, Rect Bounds);
