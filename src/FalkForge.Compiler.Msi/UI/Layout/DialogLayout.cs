using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// A named, sized canvas containing one or more uniquely-named <see cref="DialogRegion"/>s.
/// </summary>
/// <remarks>
/// The canvas defaults to MSI's standard 370 x 270 DLU. Region lookup is backed by a
/// <see cref="FrozenDictionary{TKey,TValue}"/> built once at construction so repeated
/// queries during compilation are allocation-free.
/// </remarks>
public sealed record DialogLayout
{
    private readonly string name = string.Empty;
    private readonly int canvasWidth = 370;
    private readonly int canvasHeight = 270;
    private readonly ImmutableArray<DialogRegion> regions;
    private readonly FrozenDictionary<string, int> regionIndex = FrozenDictionary<string, int>.Empty;

    /// <summary>Layout identifier; same identifier rules as <see cref="DialogRegion.Name"/>.</summary>
    public required string Name
    {
        get => this.name;
        init
        {
            DialogRegion.ValidateIdentifier(value, nameof(Name));
            this.name = value;
        }
    }

    /// <summary>Canvas width in DLU. Must be positive. Defaults to 370 (MSI standard).</summary>
    public int CanvasWidth
    {
        get => this.canvasWidth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.canvasWidth = value;
        }
    }

    /// <summary>Canvas height in DLU. Must be positive. Defaults to 270 (MSI standard).</summary>
    public int CanvasHeight
    {
        get => this.canvasHeight;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.canvasHeight = value;
        }
    }

    /// <summary>Regions belonging to this layout. Must contain at least one element with unique names.</summary>
    public required ImmutableArray<DialogRegion> Regions
    {
        get => this.regions;
        init
        {
            if (value.IsDefault || value.IsEmpty)
            {
                throw new ArgumentException("DialogLayout requires at least one region.", nameof(Regions));
            }

            this.regions = value;
            this.regionIndex = BuildIndex(value);
        }
    }

    /// <summary>O(1) name → index lookup table for the regions.</summary>
    public FrozenDictionary<string, int> RegionIndex => this.regionIndex;

    /// <summary>Attempts to look up a region by name (case-sensitive).</summary>
    public bool TryGetRegion(string name, out DialogRegion region)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (this.regionIndex.TryGetValue(name, out var index))
        {
            region = this.regions[index];
            return true;
        }

        region = default!;
        return false;
    }

    /// <summary>Returns a new layout with the region matching <paramref name="regionName"/> replaced.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="regionName"/> is not present.</exception>
    public DialogLayout With(string regionName, DialogRegion replacement)
    {
        ArgumentNullException.ThrowIfNull(regionName);
        ArgumentNullException.ThrowIfNull(replacement);

        if (!this.regionIndex.TryGetValue(regionName, out var index))
        {
            throw new ArgumentException(
                $"Region '{regionName}' is not present in layout '{this.name}'.",
                nameof(regionName));
        }

        var rebuilt = this.regions.SetItem(index, replacement);

        return this with { Regions = rebuilt };
    }

    private static FrozenDictionary<string, int> BuildIndex(ImmutableArray<DialogRegion> regions)
    {
        // Manual loop avoids LINQ allocations and lets us detect duplicates in the same pass.
        var builder = new Dictionary<string, int>(regions.Length, StringComparer.Ordinal);
        for (var i = 0; i < regions.Length; i++)
        {
            var region = regions[i];
            if (!builder.TryAdd(region.Name, i))
            {
                throw new ArgumentException(
                    $"Duplicate region name '{region.Name}' in layout.",
                    nameof(regions));
            }
        }

        return builder.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
