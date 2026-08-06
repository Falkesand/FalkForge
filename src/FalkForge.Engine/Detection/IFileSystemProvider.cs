namespace FalkForge.Engine.Detection;

public interface IFileSystemProvider
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Version? GetFileVersion(string path);

    /// <summary>
    /// Returns the full paths of the immediate child directories of <paramref name="path"/>, or an
    /// empty list when <paramref name="path"/> does not exist -- implementations must never throw for
    /// a missing directory, since <c>SearchConditionEvaluator</c> calls this on a resolved
    /// <c>dotnet</c> root that may not exist on every machine.
    /// </summary>
    IReadOnlyList<string> GetDirectories(string path);
}
