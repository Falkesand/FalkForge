using FalkForge.Extensibility;

namespace FalkForge.Extensions.Util.XmlConfig;

public sealed class XmlConfigTableContributor : IMsiTableContributor
{
    private readonly List<XmlConfigModel> _entries = [];

    public string TableName => "XmlConfig";

    /// <inheritdoc/>
    public IReadOnlyList<ContributedColumn> WriteColumns { get; } =
    [
        ContributedColumn.Key("Id"),
        ContributedColumn.Text("File"),
        ContributedColumn.Text("XPath"),
        ContributedColumn.Int("Action"),
        ContributedColumn.Text("ElementName"),
        ContributedColumn.Text("AttributeName"),
        ContributedColumn.Text("Value"),
        ContributedColumn.Int("Sequence"),
        ContributedColumn.Text("Component_", 72),
    ];

    /// <summary>Exposes the registered XmlConfig models for validation.</summary>
    public IReadOnlyList<XmlConfigModel> Items => _entries;

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>(_entries.Count);

        foreach (var entry in _entries.OrderBy(e => e.Sequence))
        {
            var row = new MsiTableRow()
                .Set("Id", entry.Id)
                .Set("File", entry.FilePath)
                .Set("XPath", entry.XPath)
                .Set("Action", (int)entry.Action)
                .Set("ElementName", entry.ElementName)
                .Set("AttributeName", entry.AttributeName)
                .Set("Value", entry.Value)
                .Set("Sequence", entry.Sequence)
                .Set("Component_", entry.ComponentRef);

            rows.Add(row);
        }

        return rows;
    }

    public void Add(XmlConfigModel entry)
    {
        _entries.Add(entry);
    }

    public void AddRange(IEnumerable<XmlConfigModel> entries)
    {
        _entries.AddRange(entries);
    }
}