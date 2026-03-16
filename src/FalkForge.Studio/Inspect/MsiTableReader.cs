using System.IO;
using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;

namespace FalkForge.Studio.Inspect;

/// <summary>
/// Reads MSI database tables for inspection purposes. All operations are read-only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiTableReader
{
    /// <summary>
    /// Opens an MSI file and returns all table names.
    /// </summary>
    public static Result<List<string>> GetTableNames(string msiPath)
    {
        if (!File.Exists(msiPath))
            return Result<List<string>>.Failure(ErrorKind.FileNotFound, $"MSI file not found: '{msiPath}'.");

        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<List<string>>.Failure(ErrorKind.IoError, $"Cannot open MSI file: {dbResult.Error.Message}");

        using var db = dbResult.Value;

        var tablesResult = db.QueryRows("SELECT `Name` FROM `_Tables`", 1);
        if (tablesResult.IsFailure)
            return Result<List<string>>.Failure(ErrorKind.IoError, $"Cannot read table list: {tablesResult.Error.Message}");

        var names = new List<string>();
        foreach (var row in tablesResult.Value)
        {
            if (row[0] is { } name)
                names.Add(name);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// Reads a specific table from an MSI file, returning column names and all rows.
    /// </summary>
    public static Result<MsiTableData> ReadTable(string msiPath, string tableName)
    {
        if (!File.Exists(msiPath))
            return Result<MsiTableData>.Failure(ErrorKind.FileNotFound, $"MSI file not found: '{msiPath}'.");

        if (string.IsNullOrWhiteSpace(tableName))
            return Result<MsiTableData>.Failure(ErrorKind.Validation, "Table name must not be empty.");

        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiTableData>.Failure(ErrorKind.IoError, $"Cannot open MSI file: {dbResult.Error.Message}");

        using var db = dbResult.Value;

        // Get column names from _Columns
        var columns = GetColumnNames(db, tableName);
        if (columns.Count == 0)
            return Result<MsiTableData>.Failure(ErrorKind.IoError, $"Table '{tableName}' has no columns or does not exist.");

        // Query all rows
        var columnList = string.Join(", ", columns.Select(c => $"`{c}`"));
        var sql = $"SELECT {columnList} FROM `{tableName}`";
        var rowsResult = db.QueryRows(sql, (uint)columns.Count);
        if (rowsResult.IsFailure)
            return Result<MsiTableData>.Failure(ErrorKind.IoError, $"Cannot read table '{tableName}': {rowsResult.Error.Message}");

        var rows = new List<List<string>>();
        foreach (var row in rowsResult.Value)
        {
            var rowData = new List<string>(row.Length);
            for (var i = 0; i < row.Length; i++)
                rowData.Add(row[i] ?? string.Empty);
            rows.Add(rowData);
        }

        return new MsiTableData(tableName, columns, rows);
    }

    private static List<string> GetColumnNames(MsiDatabase db, string tableName)
    {
        var columns = new List<string>();

        // _Columns table has: Table, Number, Name, Type
        var result = db.QueryRows(
            $"SELECT `Name` FROM `_Columns` WHERE `Table` = '{tableName}'", 1);

        if (result.IsSuccess)
        {
            foreach (var row in result.Value)
            {
                if (row[0] is { } name)
                    columns.Add(name);
            }
        }

        return columns;
    }
}
