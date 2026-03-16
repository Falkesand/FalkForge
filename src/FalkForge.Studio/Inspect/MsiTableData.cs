namespace FalkForge.Studio.Inspect;

/// <summary>
/// Represents the contents of a single MSI database table.
/// </summary>
public sealed record MsiTableData(string TableName, List<string> Columns, List<List<string>> Rows);
