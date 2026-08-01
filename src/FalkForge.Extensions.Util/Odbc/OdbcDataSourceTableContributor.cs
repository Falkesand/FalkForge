using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceTableContributor : IMsiTableContributor
{
    private readonly List<OdbcDataSourceModel> _dataSources = [];

    public string TableName => "ODBCDataSource";

    public void Add(OdbcDataSourceModel dataSource) => _dataSources.Add(dataSource);

    public IReadOnlyList<OdbcDataSourceModel> DataSources => _dataSources;

    /// <inheritdoc/>
    /// <remarks>
    /// Matches the real ODBCDataSource schema: DataSource (key), Component_, Description and
    /// DriverDescription are all non-nullable; only Registration is an integer. Component_ is
    /// non-nullable because an ODBC data source with no owning component is never installed by
    /// the MSI engine — leaving it unset must fail the build loudly, not ship a dead table.
    /// </remarks>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("DataSource"),
        ContributedColumn.Text("Component_", 72, nullable: false),
        ContributedColumn.Text("Description", nullable: false),
        ContributedColumn.Text("DriverDescription", nullable: false),
        ContributedColumn.Int("Registration"),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_dataSources.Count);

        foreach (var ds in _dataSources)
        {
            var row = new MsiTableRow()
                .Set("DataSource", ds.Id)
                .Set("Component_", ds.ComponentRef)
                .Set("Description", ds.Name)
                .Set("DriverDescription", ds.DriverName)
                .Set("Registration", (int)ds.Registration);

            rows.Add(row);
        }

        return rows;
    }
}
