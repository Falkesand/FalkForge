using FalkInstaller.Extensibility;
using FalkInstaller.Extensions.Sql.Models;

namespace FalkInstaller.Extensions.Sql;

public sealed class SqlStringTableContributor : IMsiTableContributor
{
    private readonly List<SqlStringModel> _entries = [];

    public string TableName => "SqlString";

    public void Add(SqlStringModel entry)
    {
        _entries.Add(entry);
    }

    public void AddRange(IEnumerable<SqlStringModel> entries)
    {
        _entries.AddRange(entries);
    }

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_entries.Count);

        foreach (var entry in _entries.OrderBy(e => e.Sequence))
        {
            var row = new MsiTableRow()
                .Set("Id", entry.Id)
                .Set("Database_", entry.DatabaseRef)
                .Set("Sql", entry.Sql)
                .Set("ExecuteOnInstall", entry.ExecuteOnInstall ? 1 : 0)
                .Set("ExecuteOnUninstall", entry.ExecuteOnUninstall ? 1 : 0)
                .Set("Sequence", entry.Sequence)
                .Set("ContinueOnError", entry.ContinueOnError ? 1 : 0);

            rows.Add(row);
        }

        return rows;
    }
}
