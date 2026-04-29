using System;
using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Per-region default child metrics used when a layout policy needs a fall-back size or gap.
/// </summary>
/// <remarks>
/// Defaults match MSI's standard button (56 x 17 DLU) with an 8 DLU gap. The
/// <see cref="Gaps"/> override slot supplies per-child gap values for legacy
/// templates that use non-uniform spacing — the first element is the gap before
/// child[1], the second is the gap before child[2], and so on.
/// </remarks>
public sealed record RegionDefaults
{
    private readonly int childWidth = 56;
    private readonly int childHeight = 17;
    private readonly int gap = 8;
    private readonly ImmutableArray<int> gaps = ImmutableArray<int>.Empty;

    /// <summary>Default child width in DLU. Must be positive.</summary>
    public int ChildWidth
    {
        get => this.childWidth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.childWidth = value;
        }
    }

    /// <summary>Default child height in DLU. Must be positive.</summary>
    public int ChildHeight
    {
        get => this.childHeight;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.childHeight = value;
        }
    }

    /// <summary>Default gap between adjacent children in DLU. Must be positive.</summary>
    public int Gap
    {
        get => this.gap;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.gap = value;
        }
    }

    /// <summary>
    /// Optional per-child gap overrides. Each element must be non-negative.
    /// </summary>
    public ImmutableArray<int> Gaps
    {
        get => this.gaps;
        init
        {
            if (!value.IsDefault)
            {
                foreach (var entry in value)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(entry);
                }
            }

            this.gaps = value.IsDefault ? ImmutableArray<int>.Empty : value;
        }
    }
}
