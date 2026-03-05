namespace FalkForge.Plugins.FileSystem;

public sealed class FileSystemPlugin : IInstallerPlugin
{
    public string Name => "FileSystem";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IFolderBrowser>(new FolderBrowser());
    }
}