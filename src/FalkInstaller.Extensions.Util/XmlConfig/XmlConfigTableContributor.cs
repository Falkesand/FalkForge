using FalkInstaller.Extensibility;

namespace FalkInstaller.Extensions.Util.XmlConfig;

public sealed class XmlConfigTableContributor : IMsiTableContributor
{
    private readonly List<XmlConfigModel> _entries = [];

    public string TableName => "XmlConfig";

    public void Add(XmlConfigModel entry)
    {
        _entries.Add(entry);
    }

    public void AddRange(IEnumerable<XmlConfigModel> entries)
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
}
