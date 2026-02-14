namespace FalkInstaller.Platform;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> GetFiles(string directory, string pattern, bool recursive);
    IReadOnlyList<string> GetDirectories(string directory);
    long GetFileSize(string path);
    byte[] ReadAllBytes(string path);
    Stream OpenRead(string path);
    string GetRelativePath(string relativeTo, string path);
    string GetFullPath(string path);
    string GetFileName(string path);
    string GetDirectoryName(string path);
    string GetFileHash(string path);
    DateTime GetLastWriteTimeUtc(string path);
}
