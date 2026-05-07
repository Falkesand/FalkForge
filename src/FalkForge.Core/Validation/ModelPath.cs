using System.Collections.Immutable;
using System.Text;

namespace FalkForge.Validation;

/// <summary>
/// Typed path through the model graph (e.g. "Features[2].Services[0].Name").
/// Built compositionally via fluent calls. Allocated only when a violation fires —
/// the happy path never constructs one.
/// </summary>
public readonly record struct ModelPath(ImmutableArray<PathSegment> Segments)
{
    /// <summary>The root (empty) path.</summary>
    public static readonly ModelPath Root = new(ImmutableArray<PathSegment>.Empty);

    /// <summary>Appends a named field segment.</summary>
    public ModelPath Field(string name)
        => new(Segments.Add(PathSegment.Field(name)));

    /// <summary>Appends a numeric index segment (e.g. [2]).</summary>
    public ModelPath Index(int i)
        => new(Segments.Add(PathSegment.Index(i)));

    /// <summary>Appends a string key segment (e.g. [AppVersion]).</summary>
    public ModelPath Key(string key)
        => new(Segments.Add(PathSegment.Key(key)));

    /// <summary>
    /// Renders the path as a human-readable string such as
    /// "Features[2].Services[0].Name".
    /// Dot separator inserted before a Field segment that follows any other segment.
    /// </summary>
    public override string ToString()
    {
        if (Segments.IsDefaultOrEmpty)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < Segments.Length; i++)
        {
            var seg = Segments[i];
            switch (seg.SegmentKind)
            {
                case PathSegment.Kind.Field:
                    if (i > 0)
                        sb.Append('.');
                    sb.Append(seg.Text);
                    break;
                case PathSegment.Kind.Index:
                    sb.Append('[');
                    sb.Append(seg.NumIndex);
                    sb.Append(']');
                    break;
                case PathSegment.Kind.Key:
                    sb.Append('[');
                    sb.Append(seg.Text);
                    sb.Append(']');
                    break;
            }
        }
        return sb.ToString();
    }

    // ImmutableArray<T> does not override Equals for value equality, so we need
    // a custom equality implementation. The default record Equals delegates to
    // ImmutableArray structural equality which compares references, not contents.
    public bool Equals(ModelPath other)
    {
        if (Segments.IsDefaultOrEmpty && other.Segments.IsDefaultOrEmpty)
            return true;
        if (Segments.Length != other.Segments.Length)
            return false;
        for (var i = 0; i < Segments.Length; i++)
            if (!Segments[i].Equals(other.Segments[i]))
                return false;
        return true;
    }

    public override int GetHashCode()
    {
        if (Segments.IsDefaultOrEmpty)
            return 0;
        var hash = new HashCode();
        foreach (var seg in Segments)
            hash.Add(seg);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A single segment of a <see cref="ModelPath"/>.
/// </summary>
public readonly record struct PathSegment
{
    public enum Kind : byte { Root, Field, Index, Key }

    public Kind SegmentKind { get; }
    public string? Text { get; }
    public int NumIndex { get; }

    private PathSegment(Kind kind, string? text, int numIndex)
    {
        SegmentKind = kind;
        Text = text;
        NumIndex = numIndex;
    }

    public static PathSegment Field(string name)
        => new(Kind.Field, name, 0);

    public static PathSegment Index(int i)
        => new(Kind.Index, null, i);

    public static PathSegment Key(string key)
        => new(Kind.Key, key, 0);
}
