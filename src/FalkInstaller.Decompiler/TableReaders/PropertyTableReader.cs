using FalkInstaller.Models;

namespace FalkInstaller.Decompiler.TableReaders;

/// <summary>
/// Reads the Property table from an MSI database.
/// Columns: Property, Value
/// </summary>
public static class PropertyTableReader
{
    private static readonly string[] Columns = ["Property", "Value"];

    // Properties that are internal MSI plumbing, not user-defined
    private static readonly HashSet<string> InternalProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProductCode", "ProductName", "ProductVersion", "Manufacturer",
        "UpgradeCode", "ProductLanguage", "ALLUSERS", "ARPNOMODIFY",
        "ARPNOREPAIR", "ARPNOREMOVE", "SecureCustomProperties",
        "MsiLogFileLocation", "INSTALLLEVEL", "REINSTALLMODE",
        "ROOTDRIVE", "LIMITUI", "MsiHiddenProperties"
    };

    public static Result<List<PropertyModel>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Property");
        if (existsResult.IsFailure)
            return Result<List<PropertyModel>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<PropertyModel>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Property", Columns);
        if (rowsResult.IsFailure)
            return Result<List<PropertyModel>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Property table. {rowsResult.Error.Message}");

        var properties = new List<PropertyModel>();
        foreach (var row in rowsResult.Value)
        {
            var name = row[0];
            var value = row[1];

            if (string.IsNullOrEmpty(name) || InternalProperties.Contains(name))
                continue;

            properties.Add(new PropertyModel
            {
                Name = name,
                Value = value ?? string.Empty,
                IsSecure = name == name.ToUpperInvariant(),
                IsHidden = false
            });
        }

        return properties;
    }

    /// <summary>
    /// Reads all properties including internal ones. Used by the decompiler for metadata extraction.
    /// </summary>
    public static Result<Dictionary<string, string>> ReadAll(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Property");
        if (existsResult.IsFailure)
            return Result<Dictionary<string, string>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<Dictionary<string, string>>.Success(new Dictionary<string, string>());

        var rowsResult = tableAccess.QueryTable("Property", Columns);
        if (rowsResult.IsFailure)
            return Result<Dictionary<string, string>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Property table. {rowsResult.Error.Message}");

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rowsResult.Value)
        {
            var name = row[0];
            var value = row[1];
            if (!string.IsNullOrEmpty(name))
                dict[name] = value ?? string.Empty;
        }

        return dict;
    }
}
