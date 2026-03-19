namespace FalkForge;

public sealed class InstallPath
{
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
    public IReadOnlyList<string> Segments => RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public static InstallPath operator /(InstallPath path, string subPath)
    {
        return new InstallPath(path.Root, $"{path.RelativePath}/{subPath.Replace('\\', '/').TrimEnd('/')}");
    }

    public override string ToString()
    {
        return $"[{Root.Token}]{RelativePath}";
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