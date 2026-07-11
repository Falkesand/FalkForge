using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Emits the configured IIS application pools into a custom <c>IIsAppPool</c> MSI table so the
/// pool configuration is present and inspectable in the compiled MSI. Reads the live pool list
/// from the owning <see cref="IisExtension"/> at compile time.
/// </summary>
internal sealed class IisAppPoolTableContributor(Func<IReadOnlyList<AppPoolModel>> source) : IMsiTableContributor
{
    public string TableName => "IIsAppPool";

    /// <inheritdoc/>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("AppPool"),
        ContributedColumn.Text("Name"),
        ContributedColumn.Text("ManagedRuntimeVersion", 72),
        ContributedColumn.Int("ManagedPipelineMode"),
        ContributedColumn.Int("Enable32Bit"),
        ContributedColumn.Int("IdentityType"),
        ContributedColumn.Int("MaxProcesses"),
        ContributedColumn.Int("RecycleMinutes"),
        ContributedColumn.Int("IdleTimeout"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        IReadOnlyList<AppPoolModel> pools = source();
        var rows = new List<MsiTableRow>(pools.Count);

        foreach (AppPoolModel pool in pools)
        {
            rows.Add(new MsiTableRow()
                .Set("AppPool", pool.Id)
                .Set("Name", pool.Name)
                .Set("ManagedRuntimeVersion", pool.ManagedRuntimeVersion)
                .Set("ManagedPipelineMode", (int)pool.ManagedPipelineMode)
                .Set("Enable32Bit", pool.Enable32BitAppOnWin64 ? 1 : 0)
                .Set("IdentityType", (int)pool.IdentityType)
                .Set("MaxProcesses", pool.MaxProcesses)
                .Set("RecycleMinutes", pool.RecycleMinutes)
                .Set("IdleTimeout", pool.IdleTimeoutMinutes));
        }

        return rows;
    }
}
