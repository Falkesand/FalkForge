namespace FalkForge.Engine.Detection;

public interface IFileSystemProvider
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Version? GetFileVersion(string path);
}
