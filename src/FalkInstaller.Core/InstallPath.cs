namespace FalkInstaller;

public sealed class InstallPath
{
    public KnownFolder Root { get; }
    public string RelativePath { get; }

    internal InstallPath(KnownFolder root, string relativePath)
    {
        Root = root;
        RelativePath = relativePath.Replace('\\', '/').TrimEnd('/');
    }

    public static InstallPath operator /(InstallPath path, string subPath) =>
        new(path.Root, $"{path.RelativePath}/{subPath.Replace('\\', '/').TrimEnd('/')}");

    /// <summary>
    /// Gets all directory segments from root to this path.
    /// </summary>
    public IReadOnlyList<string> Segments => RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    public override string ToString() => $"[{Root.Token}]{RelativePath}";

    public override bool Equals(object? obj) =>
        obj is InstallPath other &&
        Root.Token == other.Root.Token &&
        RelativePath == other.RelativePath;

    public override int GetHashCode() => HashCode.Combine(Root.Token, RelativePath);
}
