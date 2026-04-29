namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Determines how children placed inside a <see cref="DialogRegion"/> are positioned.
/// </summary>
public enum RegionPolicy
{
    /// <summary>Children are placed at explicit X/Y offsets within the region.</summary>
    Absolute,

    /// <summary>Children flow right-to-left from the region's right edge with a configurable gap.</summary>
    RightPacked,

    /// <summary>Children flow top-to-bottom from the region's top edge.</summary>
    TopStacked,

    /// <summary>The region holds exactly one child that fills the bounds.</summary>
    SingleControl,
}
