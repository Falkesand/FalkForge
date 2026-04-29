namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Describes a single field in a message schema: its ordinal position, identifier,
/// wire-format type, and whether it carries a presence flag for nullable values.
/// </summary>
/// <remarks>
/// Field descriptors are immutable and intended to be aggregated into an
/// <see cref="System.Collections.Immutable.ImmutableArray{T}"/> on each codec so a future
/// source generator can introspect them without runtime reflection.
/// </remarks>
public readonly record struct FieldDescriptor
{
    private readonly int _index;
    private readonly string _name;

    /// <summary>
    /// Zero-based ordinal of the field in its parent message. Must be non-negative;
    /// gaps are permitted but are deprecated.
    /// </summary>
    public int Index
    {
        get => _index;
        init
        {
            if (value < 0)
            {
                throw new ArgumentException("Field index must be non-negative.", nameof(value));
            }

            _index = value;
        }
    }

    /// <summary>
    /// Diagnostic name of the field. Used in error messages and tooling output.
    /// Must be a non-empty string.
    /// </summary>
    public string Name
    {
        get => _name;
        init
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("Field name must be non-empty.", nameof(value));
            }

            _name = value;
        }
    }

    /// <summary>The wire-format type used to encode the field.</summary>
    public WireType Type { get; init; }

    /// <summary>
    /// True when the field carries an explicit presence flag (nullable value).
    /// Should align with the chosen <see cref="WireType"/>.
    /// </summary>
    public bool Nullable { get; init; }
}
