using System.Runtime.Versioning;

namespace FalkInstaller.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    public WindowsPlatformServices()
    {
        FileSystem = new WindowsFileSystem();
        Registry = new WindowsRegistry();
        Environment = new WindowsEnvironment();
    }

    public IFileSystem FileSystem { get; }
    public IRegistry Registry { get; }
    public IEnvironment Environment { get; }
}
