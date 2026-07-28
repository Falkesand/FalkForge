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

        // Deliberately NOT validated against MsiIdentifierGrammar here (asymmetric with
        // GetColumnNames below): a hostile name that slips through is simply passed back to the
        // caller as a string to display/choose from. It only becomes a SQL-injection risk once it
        // is interpolated into MSI-SQL, which happens in ReadTable -- and ReadTable already
        // validates any tableName (whether hand-typed or echoed back from this list) before
        // building that query. Failing loud there, not here, keeps this method a pure listing.
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

        // tableName is interpolated directly into MSI-SQL below (both as a backtick-quoted
        // identifier in the outer SELECT and as a quoted literal in GetColumnNames' WHERE
        // clause). Unlike MsiTableAccess (FalkForge.Decompiler), which only ever receives
        // compile-time-constant schema names, this method is a public entry point: tableName
        // can be anything a caller (e.g. Studio's UI, echoing back a name it just read out of
        // a real, possibly hostile, MSI's own `_Tables` catalog) chooses to pass. Validate it
        // against the same canonical grammar MsiTableAccess uses before it ever reaches SQL.
        if (!MsiIdentifierGrammar.IsValidForRead(tableName))
        {
            return Result<MsiTableData>.Failure(
                ErrorKind.Validation,
                $"Table name '{tableName}' is not a valid MSI identifier: it must match the " +
                "MSI-SQL identifier grammar (a letter or underscore, followed by letters, digits, " +
                "underscores, or dots).");
        }

        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiTableData>.Failure(ErrorKind.IoError, $"Cannot open MSI file: {dbResult.Error.Message}");

        using var db = dbResult.Value;

        // Get column names from _Columns
        var columnsResult = GetColumnNames(db, tableName);
        if (columnsResult.IsFailure)
            return Result<MsiTableData>.Failure(columnsResult.Error);

        var columns = columnsResult.Value;
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

    private static Result<List<string>> GetColumnNames(MsiDatabase db, string tableName)
    {
        var columns = new List<string>();

        // _Columns table has: Table, Number, Name, Type
        var result = db.QueryRows(
            $"SELECT `Name` FROM `_Columns` WHERE `Table` = '{tableName}'", 1);

        // A genuine QueryRows failure (e.g. msi.dll couldn't open/execute the view) must not be
        // discarded: silently treating it as "zero columns" makes ReadTable report the generic,
        // misleading "has no columns or does not exist" for what is actually a real I/O/engine
        // error -- masking the true cause from the caller.
        if (result.IsFailure)
            return Result<List<string>>.Failure(result.Error);

        foreach (var row in result.Value)
        {
            if (row[0] is not { } name)
                continue;

            // Column names come straight out of the MSI's own `_Columns` catalog and get
            // interpolated into the caller's SELECT list as backtick-quoted identifiers.
            // A hand-crafted/hostile MSI (built by tooling that bypasses msi.dll's own
            // CREATE TABLE identifier validation) could plant an arbitrary string here --
            // fail loudly rather than silently dropping or passing through a bad name.
            if (!MsiIdentifierGrammar.IsValidForRead(name))
            {
                return Result<List<string>>.Failure(
                    ErrorKind.Validation,
                    $"MSI file contains an invalid column identifier '{name}' in table '{tableName}'.");
            }

            columns.Add(name);
        }

        return Result<List<string>>.Success(columns);
    }
}
