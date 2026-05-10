namespace FalkForge.Plugins.FileSystem;

/// <summary>
/// Registers the folder browser dialog service.
/// </summary>
public sealed class FileSystemPlugin : IInstallerPlugin
{
    public string Name => "FileSystem";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IFolderBrowser>(new FolderBrowser());
    }
}