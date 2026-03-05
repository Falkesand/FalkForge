using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Extensions.Util.PerfCounter;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Extensions.Util.XmlConfig;

namespace FalkForge.Extensions.Util;

public sealed class UtilExtension : IFalkForgeExtension
{
    private readonly XmlConfigTableContributor _xmlConfigContributor = new();
    private readonly ScheduledTaskTableContributor _scheduledTaskContributor = new();
    private readonly PerfCounterTableContributor _perfCounterContributor = new();
    private readonly OdbcDriverTableContributor _odbcDriverContributor = new();
    private readonly OdbcDataSourceTableContributor _odbcDataSourceContributor = new();

    public string Name => "Util";

    public XmlConfigTableContributor XmlConfig => _xmlConfigContributor;
    public ScheduledTaskTableContributor ScheduledTasks => _scheduledTaskContributor;
    public PerfCounterTableContributor PerfCounters => _perfCounterContributor;
    public OdbcDriverTableContributor OdbcDrivers => _odbcDriverContributor;
    public OdbcDataSourceTableContributor OdbcDataSources => _odbcDataSourceContributor;

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

    public void AddPerfCounter(Action<PerfCounterBuilder> configure)
    {
        var builder = new PerfCounterBuilder("PC_" + Guid.NewGuid().ToString("N")[..8]);
        configure(builder);
        _perfCounterContributor.Add(builder.Build());
    }

    public void AddPerfCounter(string id, Action<PerfCounterBuilder> configure)
    {
        var builder = new PerfCounterBuilder(id);
        configure(builder);
        _perfCounterContributor.Add(builder.Build());
    }

    public void AddOdbcDriver(Action<OdbcDriverBuilder> configure)
    {
        var builder = new OdbcDriverBuilder("ODBC_" + Guid.NewGuid().ToString("N")[..8]);
        configure(builder);
        _odbcDriverContributor.Add(builder.Build());
    }

    public void AddOdbcDriver(string id, Action<OdbcDriverBuilder> configure)
    {
        var builder = new OdbcDriverBuilder(id);
        configure(builder);
        _odbcDriverContributor.Add(builder.Build());
    }

    public void AddOdbcDataSource(Action<OdbcDataSourceBuilder> configure)
    {
        var builder = new OdbcDataSourceBuilder("DSN_" + Guid.NewGuid().ToString("N")[..8]);
        configure(builder);
        _odbcDataSourceContributor.Add(builder.Build());
    }

    public void AddOdbcDataSource(string id, Action<OdbcDataSourceBuilder> configure)
    {
        var builder = new OdbcDataSourceBuilder(id);
        configure(builder);
        _odbcDataSourceContributor.Add(builder.Build());
    }

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
        registry.RegisterTableContributor(_scheduledTaskContributor);
        registry.RegisterTableContributor(_perfCounterContributor);
        registry.RegisterTableContributor(_odbcDriverContributor);
        registry.RegisterTableContributor(_odbcDataSourceContributor);
    }
}
