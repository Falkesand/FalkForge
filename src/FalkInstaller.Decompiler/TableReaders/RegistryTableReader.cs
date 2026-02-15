using FalkInstaller.Models;

namespace FalkInstaller.Decompiler.TableReaders;

/// <summary>
/// Reads the Registry table from an MSI database.
/// Columns: Registry, Root, Key, Name, Value, Component_
/// </summary>
public static class RegistryTableReader
{
    private static readonly string[] Columns = ["Registry", "Root", "Key", "Name", "Value", "Component_"];

    public static Result<List<RegistryEntryModel>> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Registry");
        if (existsResult.IsFailure)
            return Result<List<RegistryEntryModel>>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return Result<List<RegistryEntryModel>>.Success([]);

        var rowsResult = tableAccess.QueryTable("Registry", Columns);
        if (rowsResult.IsFailure)
            return Result<List<RegistryEntryModel>>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Registry table. {rowsResult.Error.Message}");

        var entries = new List<RegistryEntryModel>();
        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[1], out var rootValue);
            var root = MapRegistryRoot(rootValue);
            var (value, valueType) = ParseRegistryValue(row[4]);

            entries.Add(new RegistryEntryModel
            {
                Root = root,
                Key = row[2] ?? string.Empty,
                ValueName = row[3],
                Value = value,
                ValueType = valueType,
                ComponentId = row[5]
            });
        }

        return entries;
    }

    internal static RegistryRoot MapRegistryRoot(int msiRoot) => msiRoot switch
    {
        0 => RegistryRoot.ClassesRoot,
        1 => RegistryRoot.CurrentUser,
        2 => RegistryRoot.LocalMachine,
        3 => RegistryRoot.Users,
        _ => RegistryRoot.LocalMachine
    };

    internal static (object? Value, RegistryValueType Type) ParseRegistryValue(string? rawValue)
    {
        if (rawValue is null)
            return (null, RegistryValueType.String);

        // MSI uses prefix markers for registry value types
        // Check #% before # to avoid ambiguity
        if (rawValue.StartsWith("#%", StringComparison.Ordinal))
        {
            // Expand string
            return (rawValue[2..], RegistryValueType.ExpandString);
        }
        else if (rawValue.StartsWith("#x", StringComparison.Ordinal))
        {
            // Hex DWORD
            if (int.TryParse(rawValue.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                return (hexVal, RegistryValueType.DWord);
        }
        else if (rawValue.StartsWith('#'))
        {
            // Decimal DWORD
            if (int.TryParse(rawValue.AsSpan(1), out var intVal))
                return (intVal, RegistryValueType.DWord);
        }
        else if (rawValue.StartsWith("[~]", StringComparison.Ordinal))
        {
            // Multi-string (append)
            return (rawValue[3..], RegistryValueType.MultiString);
        }

        return (rawValue, RegistryValueType.String);
    }
}
