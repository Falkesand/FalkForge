using FalkForge.Extensibility;
using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Emits the configured IIS web sites into a custom <c>IIsWebSite</c> MSI table so the site
/// configuration is present and inspectable in the compiled MSI. Reads the live site list from
/// the owning <see cref="IisExtension"/> at compile time.
/// <para>
/// The site's first binding (port/protocol/host/IP) is folded into the row. Sites with multiple
/// bindings currently record only their first binding here; a dedicated binding table is a
/// follow-up. Certificates are not yet emitted.
/// </para>
/// </summary>
internal sealed class IisWebSiteTableContributor(Func<IReadOnlyList<WebSiteModel>> source) : IMsiTableContributor
{
    public string TableName => "IIsWebSite";

    /// <inheritdoc/>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("WebSite"),
        ContributedColumn.Text("Description"),
        ContributedColumn.Text("Directory"),
        ContributedColumn.Text("AppPool", 72),
        ContributedColumn.Int("Port"),
        ContributedColumn.Text("Protocol", 20),
        ContributedColumn.Text("HostHeader"),
        ContributedColumn.Text("IpAddress", 64),
        ContributedColumn.Int("AutoStart"),
        ContributedColumn.Int("ConnectionTimeout"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        IReadOnlyList<WebSiteModel> sites = source();
        var rows = new List<MsiTableRow>(sites.Count);

        foreach (WebSiteModel site in sites)
        {
            WebBindingModel? binding = site.Bindings.Count > 0 ? site.Bindings[0] : null;
            rows.Add(new MsiTableRow()
                .Set("WebSite", site.Id)
                .Set("Description", site.Description)
                .Set("Directory", site.Directory)
                .Set("AppPool", site.AppPool)
                .Set("Port", binding?.Port ?? 0)
                .Set("Protocol", binding?.Protocol)
                .Set("HostHeader", binding?.HostHeader)
                .Set("IpAddress", binding?.IpAddress)
                .Set("AutoStart", site.AutoStart ? 1 : 0)
                .Set("ConnectionTimeout", site.ConnectionTimeout));
        }

        return rows;
    }
}
