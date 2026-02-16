using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;

namespace FalkForge.Decompiler;

/// <summary>
/// Production implementation of <see cref="IMsiTableAccess"/> that wraps <see cref="MsiDatabase"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiTableAccess : IMsiTableAccess
{
    private readonly MsiDatabase _database;
    private bool _disposed;

    private MsiTableAccess(MsiDatabase database)
    {
        _database = database;
    }

    public static Result<MsiTableAccess> Open(string msiPath)
    {
        if (!File.Exists(msiPath))
            return Result<MsiTableAccess>.Failure(ErrorKind.FileNotFound, $"DEC001: Cannot open MSI file '{msiPath}'. File not found.");

        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiTableAccess>.Failure(ErrorKind.IoError, $"DEC001: Cannot open MSI file '{msiPath}'. {dbResult.Error.Message}");

        return new MsiTableAccess(dbResult.Value);
    }

    public Result<List<string?[]>> QueryTable(string tableName, string[] columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIdentifier(tableName);
        foreach (var column in columns)
            ValidateIdentifier(column);

        var columnList = string.Join(", ", columns.Select(c => $"`{c}`"));
        var sql = $"SELECT {columnList} FROM `{tableName}`";
        return _database.QueryRows(sql, (uint)columns.Length);
    }

    public Result<string?> GetSummaryProperty(int propertyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Summary information access is handled through the database handle
        // For now, return null for properties we can't easily read
        return Result<string?>.Success(null);
    }

    public Result<bool> TableExists(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateIdentifier(tableName);

        // Try to query the table; if it fails, the table doesn't exist
        var sql = $"SELECT * FROM `{tableName}`";
        var result = _database.QueryRows(sql, 1);
        return Result<bool>.Success(result.IsSuccess);
    }

    private static void ValidateIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        foreach (var c in identifier)
        {
            if (c == '`' || c == ';' || c == '\'' || c == '"' || char.IsControl(c))
            {
                throw new ArgumentException(
                    $"Identifier '{identifier}' contains invalid character '{c}'.",
                    nameof(identifier));
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _database.Dispose();
            _disposed = true;
        }
    }
}
