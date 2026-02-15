using FalkInstaller.Decompiler;

namespace FalkInstaller.Decompiler.Tests;

/// <summary>
/// Mock implementation of <see cref="IMsiTableAccess"/> for unit testing.
/// Allows configuring table data without actual MSI files.
/// </summary>
public sealed class MockMsiTableAccess : IMsiTableAccess
{
    private readonly Dictionary<string, List<string?[]>> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string?> _summaryProperties = [];
    private bool _disposed;

    public MockMsiTableAccess WithTable(string tableName, List<string?[]> rows)
    {
        _tables[tableName] = rows;
        return this;
    }

    public MockMsiTableAccess WithSummaryProperty(int propertyId, string? value)
    {
        _summaryProperties[propertyId] = value;
        return this;
    }

    public Result<List<string?[]>> QueryTable(string tableName, string[] columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_tables.TryGetValue(tableName, out var rows))
            return Result<List<string?[]>>.Failure(ErrorKind.CompilationError, $"Table '{tableName}' does not exist.");

        return rows;
    }

    public Result<string?> GetSummaryProperty(int propertyId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _summaryProperties.TryGetValue(propertyId, out var value);
        return Result<string?>.Success(value);
    }

    public Result<bool> TableExists(string tableName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _tables.ContainsKey(tableName);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
