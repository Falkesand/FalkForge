using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class MsiDatabase : IDisposable
{
    private readonly MsiDatabaseHandle _handle;
    private bool _disposed;

    private MsiDatabase(MsiDatabaseHandle handle)
    {
        _handle = handle;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _handle.Dispose();
            _disposed = true;
        }
    }

    public static Result<MsiDatabase> Create(string path)
    {
        var result = NativeMethods.MsiOpenDatabase(path, NativeMethods.MSIDBOPEN_CREATE, out var handle);
        if (result != NativeMethods.ERROR_SUCCESS)
            return Result<MsiDatabase>.Failure(ErrorKind.CompilationError,
                $"Failed to create MSI database at '{path}'. Error code: {result}");

        return new MsiDatabase(new MsiDatabaseHandle(handle));
    }

    public static Result<MsiDatabase> Open(string path, bool readOnly = false)
    {
        var mode = readOnly ? NativeMethods.MSIDBOPEN_READONLY : NativeMethods.MSIDBOPEN_TRANSACT;
        var result = NativeMethods.MsiOpenDatabase(path, mode, out var handle);
        if (result != NativeMethods.ERROR_SUCCESS)
            return Result<MsiDatabase>.Failure(ErrorKind.CompilationError,
                $"Failed to open MSI database at '{path}'. Error code: {result}");

        return new MsiDatabase(new MsiDatabaseHandle(handle));
    }

    public Result<Unit> Execute(string sql)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var viewResult = NativeMethods.MsiDatabaseOpenView(_handle.DangerousGetHandle(), sql, out var viewHandle);
        if (viewResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to open view for SQL: '{sql}'. Error code: {viewResult}");

        using var view = new MsiViewHandle(viewHandle);
        var execResult = NativeMethods.MsiViewExecute(view.DangerousGetHandle(), nint.Zero);
        if (execResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to execute SQL: '{sql}'. Error code: {execResult}");

        return Unit.Value;
    }

    public Result<Unit> InsertRow(string sql, Action<MsiRecord> setFields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var viewResult = NativeMethods.MsiDatabaseOpenView(_handle.DangerousGetHandle(), sql, out var viewHandle);
        if (viewResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Failed to open view. Error code: {viewResult}");

        using var view = new MsiViewHandle(viewHandle);
        var execResult = NativeMethods.MsiViewExecute(view.DangerousGetHandle(), nint.Zero);
        if (execResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to execute view. Error code: {execResult}");

        using var record = new MsiRecord();
        setFields(record);

        var modifyResult = NativeMethods.MsiViewModify(
            view.DangerousGetHandle(),
            NativeMethods.MsiModify.Insert,
            record.Handle.DangerousGetHandle());
        if (modifyResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to insert row. Error code: {modifyResult}");

        return Unit.Value;
    }

    public Result<Unit> SetSummaryInfo(Action<SummaryInfoWriter> configure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = NativeMethods.MsiGetSummaryInformation(
            _handle.DangerousGetHandle(), nint.Zero, 20, out var summaryHandle);
        if (result != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to get summary information. Error code: {result}");

        using var handle = new MsiDatabaseHandle(summaryHandle);
        var writer = new SummaryInfoWriter(handle);
        configure(writer);

        var persistResult = NativeMethods.MsiSummaryInfoPersist(handle.DangerousGetHandle());
        if (persistResult != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError,
                $"Failed to persist summary information. Error code: {persistResult}");

        return Unit.Value;
    }

    public Result<Unit> Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = NativeMethods.MsiDatabaseCommit(_handle.DangerousGetHandle());
        if (result != NativeMethods.ERROR_SUCCESS)
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Failed to commit database. Error code: {result}");

        return Unit.Value;
    }

    public Result<List<string?[]>> QueryRows(string sql, uint fieldCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var viewResult = NativeMethods.MsiDatabaseOpenView(_handle.DangerousGetHandle(), sql, out var viewHandle);
        if (viewResult != NativeMethods.ERROR_SUCCESS)
            return Result<List<string?[]>>.Failure(ErrorKind.CompilationError,
                $"Failed to open view for SQL: '{sql}'. Error code: {viewResult}");

        using var view = new MsiViewHandle(viewHandle);
        var execResult = NativeMethods.MsiViewExecute(view.DangerousGetHandle(), nint.Zero);
        if (execResult != NativeMethods.ERROR_SUCCESS)
            return Result<List<string?[]>>.Failure(ErrorKind.CompilationError,
                $"Failed to execute SQL: '{sql}'. Error code: {execResult}");

        var rows = new List<string?[]>();
        while (true)
        {
            var fetchResult = NativeMethods.MsiViewFetch(view.DangerousGetHandle(), out var recordHandle);
            if (fetchResult == NativeMethods.ERROR_NO_MORE_ITEMS)
                break;
            if (fetchResult != NativeMethods.ERROR_SUCCESS)
                return Result<List<string?[]>>.Failure(ErrorKind.CompilationError,
                    $"Failed to fetch row. Error code: {fetchResult}");

            try
            {
                var fields = new string?[fieldCount];
                for (uint i = 0; i < fieldCount; i++) fields[i] = GetRecordString(recordHandle, i + 1);
                rows.Add(fields);
            }
            finally
            {
                _ = NativeMethods.MsiCloseHandle(recordHandle);
            }
        }

        return rows;
    }

    private static string? GetRecordString(nint hRecord, uint field)
    {
        uint size = 256;
        var buffer = new char[size + 1];
        var error = NativeMethods.MsiRecordGetString(hRecord, field, buffer, ref size);
        if (error == 234) // ERROR_MORE_DATA
        {
            size++;
            buffer = new char[size + 1];
            error = NativeMethods.MsiRecordGetString(hRecord, field, buffer, ref size);
        }

        if (error != NativeMethods.ERROR_SUCCESS)
            return null;
        return size == 0 ? null : new string(buffer, 0, (int)size);
    }

    internal nint DangerousGetHandle()
    {
        return _handle.DangerousGetHandle();
    }
}