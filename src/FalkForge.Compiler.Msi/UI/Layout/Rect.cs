using System;
using System.Runtime.InteropServices;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// An axis-aligned rectangle expressed in MSI Dialog-Layout-Units (DLU).
/// </summary>
/// <remarks>
/// Used by the layout DSL to describe canvas regions and placed control bounds.
/// <see cref="Width"/> and <see cref="Height"/> must be non-negative; zero is permitted
/// to allow degenerate spacer rectangles. <see cref="X"/> and <see cref="Y"/> may be any
/// value because regions may sit beyond canvas origin during composition.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Rect
{
    private readonly int width;
    private readonly int height;

    /// <summary>Left edge in DLU.</summary>
    public int X { get; init; }

    /// <summary>Top edge in DLU.</summary>
    public int Y { get; init; }

    /// <summary>Width in DLU. Must be non-negative.</summary>
    public int Width
    {
        get => this.width;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            this.width = value;
        }
    }

    /// <summary>Height in DLU. Must be non-negative.</summary>
    public int Height
    {
        get => this.height;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            this.height = value;
        }
    }
}
