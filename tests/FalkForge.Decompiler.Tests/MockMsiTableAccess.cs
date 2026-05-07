using FalkForge.Decompiler;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Mock implementation of <see cref="IMsiTableAccess"/> for unit testing.
/// Allows configuring table data without actual MSI files.
/// </summary>
public sealed class MockMsiTableAccess : IMsiTableAccess
{
    private readonly Dictionary<string, List<string?[]>> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tableQueryFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string?> _summaryProperties = [];
    private bool _disposed;

    public MockMsiTableAccess WithTable(string tableName, List<string?[]> rows)
    {
        _tables[tableName] = rows;
        return this;
    }

    /// <summary>
    /// Marks a table as existing (so TableExists returns true) but QueryTable returns a failure.
    /// Use this to simulate DEC003-style table read errors.
    /// </summary>
    public MockMsiTableAccess WithTableQueryFailure(string tableName, string errorMessage)
    {
        // Register the table so TableExists returns true
        _tables[tableName] = [];
        _tableQueryFailures[tableName] = errorMessage;
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

        if (_tableQueryFailures.TryGetValue(tableName, out var failMessage))
            return Result<List<string?[]>>.Failure(ErrorKind.CompilationError, failMessage);

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
