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

        // Query the _Tables system catalog to check table presence. Querying the
        // target table directly via SELECT * is unreliable: MSI may return success
        // with zero rows for a missing table, causing subsequent column-explicit
        // SELECTs to fail with error 1615. _Tables is always present and reliable.
        // MSI SQL does not support parameterised queries, so we filter in C# to
        // avoid any SQL-injection risk from the (already-validated) identifier.
        var result = _database.QueryRows("SELECT `Name` FROM `_Tables`", 1);
        if (result.IsFailure)
            return Result<bool>.Failure(result.Error);

        return Result<bool>.Success(
            result.Value.Any(row => row.Length > 0 &&
                string.Equals(row[0], tableName, StringComparison.OrdinalIgnoreCase)));
    }

    // Delegates to the canonical, shared READ-side MSI-SQL identifier grammar in
    // FalkForge.MsiIdentifierGrammar (Core) -- the same grammar FalkForge.Studio.Inspect.
    // MsiTableReader uses for the other call site that carries genuinely untrusted identifiers
    // (out of a real MSI) into interpolated MSI-SQL strings. See that type's doc comment for the
    // full read-vs-write rationale (allow-list vs. the previous incidental deny-list, and why
    // \A/\z rather than ^/$).
    private static void ValidateIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (!MsiIdentifierGrammar.IsValidForRead(identifier))
        {
            throw new ArgumentException(
                $"Identifier '{identifier}' is not a valid MSI identifier: it must match the " +
                "MSI-SQL identifier grammar (a letter or underscore, followed by letters, digits, " +
                "underscores, or dots).",
                nameof(identifier));
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
