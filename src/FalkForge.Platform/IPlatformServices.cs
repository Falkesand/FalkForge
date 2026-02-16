namespace FalkForge.Platform;

public interface IPlatformServices
{
    IFileSystem FileSystem { get; }
    IRegistry Registry { get; }
    IEnvironment Environment { get; }
}
