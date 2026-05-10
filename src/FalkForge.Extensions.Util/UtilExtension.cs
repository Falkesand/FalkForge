using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Extensions.Util.PerfCounter;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Extensions.Util.XmlConfig;
using FalkForge.Validation;

namespace FalkForge.Extensions.Util;

public sealed class UtilExtension : IFalkForgeExtension, IDryRunContributor
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

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install =>
            [
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would modify XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service,     Description = "Would create local user account(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network,     Description = "Would create file share(s)" },
                new DryRunAction { Kind = DryRunActionKind.Custom,      Description = "Would execute quiet process(es) (QuietExec)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove folder(s) on uninstall (RemoveFolderEx)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would create internet shortcut(s)" }
            ],
            DryRunIntent.Uninstall =>
            [
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would restore XML configuration file(s)" },
                new DryRunAction { Kind = DryRunActionKind.Service,     Description = "Would remove local user account(s)" },
                new DryRunAction { Kind = DryRunActionKind.Network,     Description = "Would remove file share(s)" },
                new DryRunAction { Kind = DryRunActionKind.Custom,      Description = "Would execute quiet process(es) (QuietExec rollback)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove registered folder(s) (RemoveFolderEx)" },
                new DryRunAction { Kind = DryRunActionKind.FileSystem,  Description = "Would remove internet shortcut(s)" }
            ],
            _ => []
        };

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_xmlConfigContributor);
        registry.RegisterTableContributor(_scheduledTaskContributor);
        registry.RegisterTableContributor(_perfCounterContributor);
        registry.RegisterTableContributor(_odbcDriverContributor);
        registry.RegisterTableContributor(_odbcDataSourceContributor);
    }

    /// <inheritdoc/>
    public ImmutableArray<ValidationRule> GetValidationRules()
        => UtilRules.Build(() => _xmlConfigContributor.Items);
}