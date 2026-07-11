using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Extensions.Util.PerfCounter;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Extensions.Util.XmlConfig;
using Xunit;

namespace FalkForge.Extensions.Util.Tests;

public sealed class UtilExtensionTests
{
    [Fact]
    public void Name_ReturnsUtil()
    {
        var extension = new UtilExtension();

        Assert.Equal("Util", extension.Name);
    }

    [Fact]
    public void Extension_ImplementsIFalkForgeExtension()
    {
        var extension = new UtilExtension();

        Assert.IsAssignableFrom<IFalkForgeExtension>(extension);
    }

    [Fact]
    public void Register_RegistersXmlConfigTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is XmlConfigTableContributor);
    }

    [Fact]
    public void Register_RegistersScheduledTaskTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is ScheduledTaskTableContributor);
    }

    [Fact]
    public void Register_XmlConfigContributor_HasCorrectTableName()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        var xmlContributor = registry.TableContributors.First(c => c is XmlConfigTableContributor);
        Assert.Equal("XmlConfig", xmlContributor.TableName);
    }

    [Fact]
    public void XmlConfig_ReturnsTableContributor()
    {
        var extension = new UtilExtension();

        Assert.NotNull(extension.XmlConfig);
        Assert.IsType<XmlConfigTableContributor>(extension.XmlConfig);
    }

    [Fact]
    public void XmlConfig_ReturnsSameInstanceAcrossCalls()
    {
        var extension = new UtilExtension();

        var first = extension.XmlConfig;
        var second = extension.XmlConfig;

        Assert.Same(first, second);
    }

    [Fact]
    public void Register_RegistersPerfCounterTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is PerfCounterTableContributor);
    }

    [Fact]
    public void Register_RegistersOdbcDriverTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is OdbcDriverTableContributor);
    }

    [Fact]
    public void Register_RegistersOdbcDataSourceTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is OdbcDataSourceTableContributor);
    }

    [Fact]
    public void Register_DoesNotRegisterComponentContributors()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Empty(registry.ComponentContributors);
    }

    [Fact]
    public void Register_RegistersExecutionContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Single(registry.ExecutionContributors);
    }

    [Fact]
    public void AddQuietExec_AddsModelAndFeedsExecutionContributor()
    {
        var extension = new UtilExtension();

        var result = extension.AddQuietExec(q => q.Id("Provision").Command("setup.exe /quiet"));

        Assert.True(result.IsSuccess);
        Assert.Single(extension.QuietExecs);
        Assert.Equal("Provision", extension.QuietExecs[0].Id);
    }

    [Fact]
    public void AddQuietExec_PropagatesBuilderFailure()
    {
        var extension = new UtilExtension();

        var result = extension.AddQuietExec(q => q.Id("Bad"));

        Assert.True(result.IsFailure);
        Assert.Contains("QEX002", result.Error.Message);
        Assert.Empty(extension.QuietExecs);
    }

    [Fact]
    public void AddRemoveFolderEx_AddsModel()
    {
        var extension = new UtilExtension();

        var result = extension.AddRemoveFolderEx(r => r.Id("Cache").Directory(@"C:\ProgramData\App\Cache").OnUninstall());

        Assert.True(result.IsSuccess);
        Assert.Single(extension.RemoveFolderExes);
        Assert.Equal("Cache", extension.RemoveFolderExes[0].Id);
    }

    [Fact]
    public void AddFileShare_AddsModel()
    {
        var extension = new UtilExtension();

        var result = extension.AddFileShare(f => f.Id("Data").Name("AppData").Directory(@"C:\Data"));

        Assert.True(result.IsSuccess);
        Assert.Single(extension.FileShares);
        Assert.Equal("AppData", extension.FileShares[0].Name);
    }

    [Fact]
    public void AddInternetShortcut_AddsModel()
    {
        var extension = new UtilExtension();

        var result = extension.AddInternetShortcut(s => s
            .Id("Home").Name("App Home").Target("https://example.com").Directory(@"C:\ProgramData\App"));

        Assert.True(result.IsSuccess);
        Assert.Single(extension.InternetShortcuts);
        Assert.Equal("App Home", extension.InternetShortcuts[0].Name);
    }

    private sealed class TestExtensionRegistry : IExtensionRegistry
    {
        public List<IMsiTableContributor> TableContributors { get; } = [];
        public List<IComponentContributor> ComponentContributors { get; } = [];
        public List<IExecutionContributor> ExecutionContributors { get; } = [];

        public void RegisterTableContributor(IMsiTableContributor contributor) =>
            TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor) =>
            ComponentContributors.Add(contributor);

        public void RegisterExecutionContributor(IExecutionContributor contributor) =>
            ExecutionContributors.Add(contributor);

        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
        public void RegisterDialogStep(IDialogStepBuilder builder) { }
    }
}
