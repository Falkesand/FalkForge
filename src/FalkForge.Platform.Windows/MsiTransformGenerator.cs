namespace FalkForge.Platform.Windows;

using System.Text;
using System.Runtime.Versioning;

/// <summary>
/// Builds a Windows Installer transform (<c>.mst</c>) that sets a set of secret properties, without
/// putting any plaintext on the installer command line. The transform is applied at install time via
/// <c>TRANSFORMS=</c>, so the secret stays out of the command line and out of the Windows Installer log.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>FalkForge.Compiler.Msi.TransformCompiler</c> so the NativeAOT elevation companion can
/// generate the transform itself (the companion must not reference the compiler assembly). Uses
/// <c>LibraryImport</c> bindings only.
/// </para>
/// <para>
/// Generating a transform requires a writable working copy of the base MSI on disk. This method stages
/// that copy plus the transform inside the staging directory the caller passes, which the CALLER is
/// responsible for securing: the elevated path passes a directory ACL'd to SYSTEM + Administrators only,
/// the per-user path passes a fresh unpredictable per-user temp directory. The working copy is created
/// with create-new semantics (it refuses a pre-planted name) and deleted before this method returns; only
/// the transform survives, for the caller to apply and then delete. Both the working copy and the
/// transform hold the secret in plaintext for the brief window they exist — that residual is mitigated by
/// the caller's directory protection, not eliminated.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class MsiTransformGenerator
{
    private const uint ErrorSuccess = 0;

    /// <summary>
    /// Generates a transform under <paramref name="stagingDirectory"/> that sets every entry of
    /// <paramref name="secrets"/> as an MSI property on <paramref name="baseMsiPath"/>. Returns the path
    /// to the generated <c>.mst</c>; the caller applies it via <c>TRANSFORMS=</c> and deletes it (and the
    /// staging directory) afterward.
    /// </summary>
    public static Result<string> GenerateSecretTransform(
        string baseMsiPath,
        IReadOnlyDictionary<string, SensitiveBytes> secrets,
        string stagingDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseMsiPath);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrEmpty(stagingDirectory);

        if (secrets.Count == 0)
            return Result<string>.Failure(ErrorKind.ExecutionError,
                "No secret properties to transform");

        if (!File.Exists(baseMsiPath))
            return Result<string>.Failure(ErrorKind.FileNotFound, $"Base MSI not found: '{baseMsiPath}'");

        var workingCopy = Path.Combine(stagingDirectory, $"~pw-{Guid.NewGuid():N}.msi");
        var mstPath = Path.Combine(stagingDirectory, $"st-{Guid.NewGuid():N}.mst");

        try
        {
            // Create-new semantics: overwrite:false throws if the name already exists, so a pre-planted
            // link or file at the working-copy name is refused rather than followed.
            File.Copy(baseMsiPath, workingCopy, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Failure(ErrorKind.IoError,
                $"Failed to stage a working copy of the base MSI: {ex.Message}");
        }

        var succeeded = false;
        try
        {
            var applyResult = ApplySecretProperties(workingCopy, secrets);
            if (applyResult.IsFailure)
                return Result<string>.Failure(applyResult.Error);

            var genResult = GenerateTransform(workingCopy, baseMsiPath, mstPath);
            if (genResult.IsFailure)
                return Result<string>.Failure(genResult.Error);

            succeeded = true;
            return mstPath;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<string>.Failure(ErrorKind.ExecutionError,
                $"Failed to generate the secret property transform: {ex.Message}");
        }
        finally
        {
            // The working copy carries the secret; delete it as soon as the transform is generated. On any
            // failure delete the transform too — MsiDatabaseGenerateTransform may have created it before a
            // later step failed, and the caller only tracks it on success, so it would otherwise be an
            // orphaned secret file. On success the transform outlives this method (the caller installs with
            // it, then deletes it).
            TryDelete(workingCopy);
            if (!succeeded)
                TryDelete(mstPath);
        }
    }

    /// <summary>
    /// Upserts every secret into the working copy's <c>Property</c> table: UPDATE where the row exists,
    /// INSERT where it does not. Values travel only through a bound record parameter, never interpolated
    /// into SQL text.
    /// </summary>
    private static Result<Unit> ApplySecretProperties(
        string workingCopyPath, IReadOnlyDictionary<string, SensitiveBytes> secrets)
    {
        var open = NativeMethods.MsiOpenDatabase(workingCopyPath, NativeMethods.MsiDbOpenTransact, out var db);
        if (open != ErrorSuccess)
            return Result<Unit>.Failure(ErrorKind.ExecutionError,
                $"Failed to open the working-copy MSI database. Error code: {open}");

        try
        {
            var existingResult = ReadExistingPropertyNames(db);
            if (existingResult.IsFailure)
                return Result<Unit>.Failure(existingResult.Error);

            var existing = existingResult.Value;

            foreach (var (name, secret) in secrets)
            {
                // Decode the UTF-8 plaintext the UI collected. The resulting string is immutable and cannot
                // be zeroed — an unavoidable residual of the msi.dll string-setting API.
                var value = Encoding.UTF8.GetString(secret.Span);

                var opResult = existing.Contains(name)
                    ? UpdateProperty(db, name, value)
                    : InsertProperty(db, name, value);
                if (opResult.IsFailure)
                    return opResult;
            }

            var commit = NativeMethods.MsiDatabaseCommit(db);
            if (commit != ErrorSuccess)
                return Result<Unit>.Failure(ErrorKind.ExecutionError,
                    $"Failed to commit the working-copy MSI database. Error code: {commit}");

            return Unit.Value;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(db);
        }
    }

    private static Result<HashSet<string>> ReadExistingPropertyNames(nint db)
    {
        var view = OpenView(db, "SELECT `Property` FROM `Property`");
        if (view.IsFailure)
            return Result<HashSet<string>>.Failure(view.Error);

        try
        {
            var exec = NativeMethods.MsiViewExecute(view.Value, nint.Zero);
            if (exec != ErrorSuccess)
                return Result<HashSet<string>>.Failure(ErrorKind.ExecutionError,
                    $"Failed to enumerate the Property table. Error code: {exec}");

            var names = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var fetch = NativeMethods.MsiViewFetch(view.Value, out var record);
                if (fetch == NativeMethods.ErrorNoMoreItems)
                    break;
                if (fetch != ErrorSuccess)
                    return Result<HashSet<string>>.Failure(ErrorKind.ExecutionError,
                        $"Failed to fetch a Property row. Error code: {fetch}");

                try
                {
                    var name = ReadRecordString(record, 1);
                    if (name is not null)
                        names.Add(name);
                }
                finally
                {
                    _ = NativeMethods.MsiCloseHandle(record);
                }
            }

            return names;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(view.Value);
        }
    }

    private static Result<Unit> UpdateProperty(nint db, string name, string value)
    {
        var view = OpenView(db, "UPDATE `Property` SET `Value` = ? WHERE `Property` = ?");
        if (view.IsFailure)
            return Result<Unit>.Failure(view.Error);

        try
        {
            var record = NativeMethods.MsiCreateRecord(2);
            if (record == nint.Zero)
                return Result<Unit>.Failure(ErrorKind.ExecutionError, "Failed to create an MSI record");

            try
            {
                _ = NativeMethods.MsiRecordSetString(record, 1, value);
                _ = NativeMethods.MsiRecordSetString(record, 2, name);
                var exec = NativeMethods.MsiViewExecute(view.Value, record);
                if (exec != ErrorSuccess)
                    return Result<Unit>.Failure(ErrorKind.ExecutionError,
                        $"Failed to update property '{name}'. Error code: {exec}");
            }
            finally
            {
                _ = NativeMethods.MsiCloseHandle(record);
            }

            return Unit.Value;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(view.Value);
        }
    }

    private static Result<Unit> InsertProperty(nint db, string name, string value)
    {
        var view = OpenView(db, "SELECT `Property`, `Value` FROM `Property`");
        if (view.IsFailure)
            return Result<Unit>.Failure(view.Error);

        try
        {
            var exec = NativeMethods.MsiViewExecute(view.Value, nint.Zero);
            if (exec != ErrorSuccess)
                return Result<Unit>.Failure(ErrorKind.ExecutionError,
                    $"Failed to execute the Property view for insert. Error code: {exec}");

            var record = NativeMethods.MsiCreateRecord(2);
            if (record == nint.Zero)
                return Result<Unit>.Failure(ErrorKind.ExecutionError, "Failed to create an MSI record");

            try
            {
                _ = NativeMethods.MsiRecordSetString(record, 1, name);
                _ = NativeMethods.MsiRecordSetString(record, 2, value);
                var modify = NativeMethods.MsiViewModify(view.Value, NativeMethods.MsiModifyInsert, record);
                if (modify != ErrorSuccess)
                    return Result<Unit>.Failure(ErrorKind.ExecutionError,
                        $"Failed to insert property '{name}'. Error code: {modify}");
            }
            finally
            {
                _ = NativeMethods.MsiCloseHandle(record);
            }

            return Unit.Value;
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(view.Value);
        }
    }

    private static Result<Unit> GenerateTransform(string targetPath, string basePath, string mstPath)
    {
        var openTarget = NativeMethods.MsiOpenDatabase(targetPath, NativeMethods.MsiDbOpenReadOnly, out var target);
        if (openTarget != ErrorSuccess)
            return Result<Unit>.Failure(ErrorKind.ExecutionError,
                $"Failed to open the transform target database. Error code: {openTarget}");

        try
        {
            var openBase = NativeMethods.MsiOpenDatabase(basePath, NativeMethods.MsiDbOpenReadOnly, out var baseDb);
            if (openBase != ErrorSuccess)
                return Result<Unit>.Failure(ErrorKind.ExecutionError,
                    $"Failed to open the transform reference database. Error code: {openBase}");

            try
            {
                var gen = NativeMethods.MsiDatabaseGenerateTransform(target, baseDb, mstPath, 0, 0);
                if (gen != ErrorSuccess)
                    return Result<Unit>.Failure(ErrorKind.ExecutionError,
                        $"Failed to generate the transform. Error code: {gen}");

                var summary = NativeMethods.MsiCreateTransformSummaryInfo(target, baseDb, mstPath, 0, 0);
                if (summary != ErrorSuccess)
                    return Result<Unit>.Failure(ErrorKind.ExecutionError,
                        $"Failed to create transform summary info. Error code: {summary}");

                return Unit.Value;
            }
            finally
            {
                _ = NativeMethods.MsiCloseHandle(baseDb);
            }
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(target);
        }
    }

    private static Result<nint> OpenView(nint db, string sql)
    {
        var open = NativeMethods.MsiDatabaseOpenView(db, sql, out var view);
        if (open != ErrorSuccess)
            return Result<nint>.Failure(ErrorKind.ExecutionError,
                $"Failed to open a view. Error code: {open}");
        return view;
    }

    private static string? ReadRecordString(nint record, uint field)
    {
        uint size = 256;
        var buffer = new char[size + 1];
        var error = NativeMethods.MsiRecordGetString(record, field, buffer, ref size);
        if (error == NativeMethods.ErrorMoreData)
        {
            size++;
            buffer = new char[size + 1];
            error = NativeMethods.MsiRecordGetString(record, field, buffer, ref size);
        }

        if (error != ErrorSuccess)
            return null;
        return size == 0 ? null : new string(buffer, 0, (int)size);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a failure to delete must never mask the real result.
        }
    }
}
