namespace FalkForge;

public sealed class InstallPath
{
    // InstallPath is immutable, so both are safe to compute once and reuse:
    // Segments is re-split and ToString() is re-formatted on every access
    // otherwise, and both are read repeatedly (per-segment, per-producer) while
    // walking the synthesized directory tree.
    private string[]? _segments;
    private string? _toString;

    internal InstallPath(KnownFolder root, string relativePath)
    {
        Root = root;
        RelativePath = relativePath.Replace('\\', '/').TrimEnd('/');
    }

    public KnownFolder Root { get; }
    public string RelativePath { get; }

    /// <summary>
    ///     Gets all directory segments from root to this path.
    /// </summary>
    public IReadOnlyList<string> Segments => _segments ??= RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public static InstallPath operator /(InstallPath path, string subPath)
    {
        return new InstallPath(path.Root, $"{path.RelativePath}/{subPath.Replace('\\', '/').TrimEnd('/')}");
    }

    public override string ToString()
    {
        return _toString ??= $"[{Root.Token}]{RelativePath}";
    }

    public override bool Equals(object? obj)
    {
        return obj is InstallPath other &&
               Root.Token == other.Root.Token &&
               RelativePath == other.RelativePath;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Root.Token, RelativePath);
    }
}