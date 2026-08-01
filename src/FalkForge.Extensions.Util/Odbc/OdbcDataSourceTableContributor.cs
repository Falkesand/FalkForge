using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceTableContributor : IMsiTableContributor
{
    private const string DerivedComponentHint =
        "ODB001: ODBCDataSource.Component_ is derived — from the component owning the DLL of the " +
        "matching AddOdbcDriver(...) entry, otherwise from the package's first component. A package " +
        "with no components at all cannot install a data source; declare at least one file.";

    private readonly List<OdbcDataSourceModel> _dataSources = [];
    private readonly OdbcDriverTableContributor? _drivers;

    public OdbcDataSourceTableContributor()
    {
    }

    /// <summary>
    /// Creates a contributor that can attach each data source to the component owning the DLL of
    /// the driver it names, rather than to the package's first component.
    /// </summary>
    public OdbcDataSourceTableContributor(OdbcDriverTableContributor drivers) => _drivers = drivers;

    public string TableName => "ODBCDataSource";

    public void Add(OdbcDataSourceModel dataSource) => _dataSources.Add(dataSource);

    public IReadOnlyList<OdbcDataSourceModel> DataSources => _dataSources;

    /// <inheritdoc/>
    /// <remarks>
    /// Matches the real ODBCDataSource schema: DataSource (key), Component_, Description and
    /// DriverDescription are all non-nullable, and Registration is a SHORT. Component_ is
    /// non-nullable because an ODBC data source with no owning component is never installed by
    /// the MSI engine — leaving it unset must fail the build loudly, not ship a dead table.
    /// </remarks>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("DataSource"),
        new ContributedColumn
        {
            Name = "Component_",
            Type = ContributedColumnType.String,
            Width = 72,
            MissingValueHint = DerivedComponentHint,
        },
        ContributedColumn.Text("Description", nullable: false),
        ContributedColumn.Text("DriverDescription", nullable: false),
        new ContributedColumn { Name = "Registration", Type = ContributedColumnType.Int16 },
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rows = new List<MsiTableRow>(_dataSources.Count);

        foreach (OdbcDataSourceModel ds in _dataSources)
        {
            rows.Add(new MsiTableRow()
                .Set("DataSource", ds.Id)
                .Set("Component_", ResolveComponent(context, ds))
                .Set("Description", ds.Name)
                .Set("DriverDescription", ds.DriverName)
                .Set("Registration", (int)ds.Registration));
        }

        return rows;
    }

    /// <summary>
    /// A data source installs and uninstalls with its owning component. Preferred owner is the
    /// component carrying the DLL of the driver this DSN names, so removing the driver removes the
    /// DSN with it. A DSN may legitimately target a driver the machine already has, in which case
    /// there is nothing to key off and the package's first component is used — the same fallback
    /// the built-in <c>CreateFolder</c> producer applies.
    /// </summary>
    private string? ResolveComponent(ExtensionContext context, OdbcDataSourceModel dataSource)
    {
        string? driverComponent = _drivers?.TryResolveComponentFor(context, dataSource.DriverName);
        if (driverComponent is not null)
            return driverComponent;

        return context.ResolvedComponentIds.Count > 0 ? context.ResolvedComponentIds[0] : null;
    }
}
