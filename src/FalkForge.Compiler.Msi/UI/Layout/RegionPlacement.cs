using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// The set of <see cref="PlacedControl"/>s assigned to a single named region of a
/// <see cref="DialogLayout"/>.
/// </summary>
/// <remarks>
/// <see cref="RegionName"/> must match the name of an existing region in the target layout
/// — verification happens during composition (phase 5+).
/// </remarks>
public sealed record RegionPlacement
{
    /// <summary>The region this placement targets. Must match a region in the target layout.</summary>
    public required string RegionName { get; init; }

    /// <summary>Controls assigned to the region. May be empty.</summary>
    public ImmutableArray<PlacedControl> Controls { get; init; } = ImmutableArray<PlacedControl>.Empty;
}
