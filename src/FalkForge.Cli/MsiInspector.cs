using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;

namespace FalkForge.Cli;

/// <summary>
/// Inspects an MSI database and extracts metadata without modifying the file.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiInspector
{
    /// <summary>
    /// Opens an MSI file read-only and extracts summary metadata.
    /// </summary>
    public static Result<MsiInspectionResult> Inspect(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiInspectionResult>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        // Read key properties from the Property table
        string? productName = null;
        string? manufacturer = null;
        string? version = null;
        string? productCode = null;

        var propertyResult = db.QueryRows("SELECT `Property`, `Value` FROM `Property`", 2);
        if (propertyResult.IsSuccess)
        {
            foreach (var row in propertyResult.Value)
            {
                switch (row[0])
                {
                    case "ProductName":
                        productName = row[1];
                        break;
                    case "Manufacturer":
                        manufacturer = row[1];
                        break;
                    case "ProductVersion":
                        version = row[1];
                        break;
                    case "ProductCode":
                        productCode = row[1];
                        break;
                }
            }
        }

        // Enumerate tables using _Tables
        var tableNames = new List<string>();
        var tablesResult = db.QueryRows("SELECT `Name` FROM `_Tables`", 1);
        if (tablesResult.IsSuccess)
        {
            foreach (var row in tablesResult.Value)
            {
                if (row[0] is { } name)
                    tableNames.Add(name);
            }
        }

        return new MsiInspectionResult
        {
            ProductName = productName,
            Manufacturer = manufacturer,
            Version = version,
            ProductCode = productCode,
            TableNames = tableNames,
            TableCount = tableNames.Count
        };
    }
}
