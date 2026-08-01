using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

/// <summary>
/// Emits <c>ODBCSourceAttribute</c> rows for every key/value pair attached via
/// <see cref="OdbcDataSourceBuilder.Property"/>. Without this contributor, connection attributes
/// (e.g. Server, Database) set on a data source were silently dropped: the model carried them,
/// but nothing ever turned them into rows the ODBC driver manager reads at install time.
/// <para>
/// Matches the real ODBCSourceAttribute schema: <c>DataSource_</c> and <c>Attribute</c> form a
/// composite primary key (both required, per row), and <c>Value</c> is nullable.
/// </para>
/// </summary>
internal sealed class OdbcSourceAttributeTableContributor : IMsiTableContributor
{
    private readonly OdbcDataSourceTableContributor _dataSources;

    internal OdbcSourceAttributeTableContributor(OdbcDataSourceTableContributor dataSources)
    {
        _dataSources = dataSources;
    }

    public string TableName => "ODBCSourceAttribute";

    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("DataSource_"),
        new ContributedColumn { Name = "Attribute", Type = ContributedColumnType.String, Width = 255, PrimaryKey = true },
        ContributedColumn.Text("Value", nullable: true),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        foreach (var ds in _dataSources.DataSources)
        {
            foreach (var (attribute, value) in ds.Properties)
            {
                rows.Add(new MsiTableRow()
                    .Set("DataSource_", ds.Id)
                    .Set("Attribute", attribute)
                    .Set("Value", value));
            }
        }

        return rows;
    }
}
