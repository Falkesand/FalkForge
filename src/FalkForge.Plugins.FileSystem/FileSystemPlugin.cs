namespace FalkForge.Plugins.FileSystem;

using FalkForge.Plugins;

public sealed class FileSystemPlugin : IInstallerPlugin
{
    public string Name => "FileSystem";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IFolderBrowser>(new FolderBrowser());
    }
}
