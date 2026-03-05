namespace FalkForge.Extensibility;

public interface IMsiTableContributor
{
    string TableName { get; }
    IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context);
}