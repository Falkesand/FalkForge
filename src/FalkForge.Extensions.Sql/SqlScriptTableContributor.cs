using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

public sealed class SqlScriptTableContributor : IMsiTableContributor
{
    private readonly List<SqlScriptModel> _entries = [];

    public string TableName => "SqlScript";

    /// <summary>Exposes the registered script models for validation.</summary>
    public IReadOnlyList<SqlScriptModel> Items => _entries;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_entries.Count);

        foreach (var entry in _entries.OrderBy(e => e.Sequence))
        {
            var row = new MsiTableRow()
                .Set("Id", entry.Id)
                .Set("Database_", entry.DatabaseRef)
                .Set("SourceFile", entry.SourceFile)
                .Set("SqlContent", entry.SqlContent)
                .Set("ExecuteOnInstall", entry.ExecuteOnInstall ? 1 : 0)
                .Set("ExecuteOnReinstall", entry.ExecuteOnReinstall ? 1 : 0)
                .Set("ExecuteOnUninstall", entry.ExecuteOnUninstall ? 1 : 0)
                .Set("RollbackSourceFile", entry.RollbackSourceFile)
                .Set("Sequence", entry.Sequence)
                .Set("ContinueOnError", entry.ContinueOnError ? 1 : 0)
                .Set("Component_", entry.ComponentRef);

            rows.Add(row);
        }

        return rows;
    }

    public void Add(SqlScriptModel entry)
    {
        _entries.Add(entry);
    }

    public void AddRange(IEnumerable<SqlScriptModel> entries)
    {
        _entries.AddRange(entries);
    }
}