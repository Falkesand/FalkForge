namespace FalkForge.Plugins.FileSystem.Tests;

using FalkForge.Plugins;
using Xunit;

public sealed class FileSystemPluginTests
{
    [Fact]
    public void Plugin_registers_IFolderBrowser()
    {
        var registry = new PluginServiceRegistry();
        var plugin = new FileSystemPlugin();
        plugin.RegisterServices(registry);

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IFolderBrowser>());
    }

    [Fact]
    public void Name_is_FileSystem()
    {
        var plugin = new FileSystemPlugin();
        Assert.Equal("FileSystem", plugin.Name);
    }
}
