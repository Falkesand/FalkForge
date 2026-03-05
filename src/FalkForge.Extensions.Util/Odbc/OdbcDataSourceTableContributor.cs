using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.Odbc;

public sealed class OdbcDataSourceTableContributor : IMsiTableContributor
{
    private readonly List<OdbcDataSourceModel> _dataSources = [];

    public string TableName => "ODBCDataSource";

    public void Add(OdbcDataSourceModel dataSource) => _dataSources.Add(dataSource);

    public IReadOnlyList<OdbcDataSourceModel> DataSources => _dataSources;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_dataSources.Count);

        foreach (var ds in _dataSources)
        {
            var row = new MsiTableRow()
                .Set("DataSource", ds.Id)
                .Set("Description", ds.Name)
                .Set("DriverDescription", ds.DriverName)
                .Set("Registration", (int)ds.Registration);

            rows.Add(row);
        }

        return rows;
    }
}
