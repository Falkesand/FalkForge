using System;
using System.Text.RegularExpressions;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// A named rectangular region within a <see cref="DialogLayout"/> that owns a placement policy.
/// </summary>
/// <remarks>
/// Names follow C-style identifier rules (<c>^[A-Za-z_][A-Za-z0-9_]*$</c>) so that
/// regions can be referenced from fluent customization APIs without quoting.
/// </remarks>
public sealed partial record DialogRegion
{
    private static readonly Regex IdentifierRegex = CreateIdentifierRegex();

    private readonly string name = string.Empty;

    /// <summary>Region identifier; must match the C-style identifier pattern.</summary>
    public required string Name
    {
        get => this.name;
        init
        {
            ValidateIdentifier(value, nameof(Name));
            this.name = value;
        }
    }

    /// <summary>Region bounds in canvas DLU.</summary>
    public required Rect Bounds { get; init; }

    /// <summary>Placement policy for child controls.</summary>
    public required RegionPolicy Policy { get; init; }

    /// <summary>Per-region default child metrics.</summary>
    public RegionDefaults Defaults { get; init; } = new RegionDefaults();

    internal static void ValidateIdentifier(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Region or layout name must be non-empty.", paramName);
        }

        if (!IdentifierRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"Name '{value}' is not a valid identifier; expected ^[A-Za-z_][A-Za-z0-9_]*$.",
                paramName);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CreateIdentifierRegex();
}
