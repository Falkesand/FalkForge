using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

public sealed class SqlDatabaseTableContributor : IMsiTableContributor
{
    private readonly List<SqlDatabaseModel> _entries = [];

    public string TableName => "SqlDatabase";

    public void Add(SqlDatabaseModel entry)
    {
        _entries.Add(entry);
    }

    public void AddRange(IEnumerable<SqlDatabaseModel> entries)
    {
        _entries.AddRange(entries);
    }

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_entries.Count);

        foreach (var entry in _entries)
        {
            var row = new MsiTableRow()
                .Set("Id", entry.Id)
                .Set("Server", entry.Server)
                .Set("Database", entry.Database)
                .Set("Instance", entry.Instance)
                .Set("ConnectionString", entry.ConnectionString)
                .Set("CreateOnInstall", entry.CreateOnInstall ? 1 : 0)
                .Set("DropOnUninstall", entry.DropOnUninstall ? 1 : 0)
                .Set("ConfirmOverwrite", entry.ConfirmOverwrite ? 1 : 0)
                .Set("Component_", entry.ComponentRef);

            rows.Add(row);
        }

        return rows;
    }
}
