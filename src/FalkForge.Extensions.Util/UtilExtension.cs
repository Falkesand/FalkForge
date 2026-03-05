using FalkForge.Extensibility;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Extensions.Util.XmlConfig;

namespace FalkForge.Extensions.Util;

public sealed class UtilExtension : IFalkForgeExtension
{
    private readonly XmlConfigTableContributor _xmlConfigContributor = new();
    private readonly ScheduledTaskTableContributor _scheduledTaskContributor = new();

    public string Name => "Util";

    public XmlConfigTableContributor XmlConfig => _xmlConfigContributor;
    public ScheduledTaskTableContributor ScheduledTasks => _scheduledTaskContributor;

    public void AddScheduledTask(Action<ScheduledTaskBuilder> configure)
    {
        var builder = new ScheduledTaskBuilder("ST_" + Guid.NewGuid().ToString("N")[..8]);
        configure(builder);
        _scheduledTaskContributor.Add(builder.Build());
    }

    public void AddScheduledTask(string id, Action<ScheduledTaskBuilder> configure)
    {
        var builder = new ScheduledTaskBuilder(id);
        configure(builder);
        _scheduledTaskContributor.Add(builder.Build());
    }

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
        registry.RegisterTableContributor(_scheduledTaskContributor);
    }
}
