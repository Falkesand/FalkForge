using System.Buffers;
using System.Runtime.InteropServices;
using FalkForge.Engine.Elevation.Interop;
using Microsoft.Win32.SafeHandles;

namespace FalkForge.Engine.Elevation.Commands;

/// <summary>
/// Handle-based no-follow file write for the elevated (SYSTEM) process. This is the INNER
/// enforcement behind the outer path-policy gate (<see cref="ElevatedPathPolicy"/>): it must
/// hold even when a path component is swapped for a junction/symlink AFTER the policy walk.
/// </summary>
/// <remarks>
/// Mechanism (CreateFileW parent-handle pin + no-follow leaf + post-open handle verification;
/// NtCreateFile-relative was judged disproportionate for this command — see residual below):
/// <list type="number">
/// <item>The parent directory is opened by HANDLE with <c>FILE_FLAG_OPEN_REPARSE_POINT</c>
/// (a junction at the final component is opened as itself, never traversed) and WITHOUT
/// <c>FILE_SHARE_DELETE</c>, so the parent cannot be renamed or deleted while the handle is
/// held; NTFS likewise refuses to rename any ancestor of an open handle.</item>
/// <item>The parent handle is verified: not a reparse point, and its true path (via
/// <c>GetFinalPathNameByHandle</c>) equals the expected path — this detects a path that
/// resolved THROUGH an ancestor junction planted after the policy walk.</item>
/// <item>The target is opened with <c>FILE_FLAG_OPEN_REPARSE_POINT</c> and <c>OPEN_ALWAYS</c>
/// (no truncation at open), then verified the same way: a file symlink at the leaf — dangling
/// or not — is opened as itself, detected via its reparse attribute, and rejected; a final-path
/// mismatch is rejected before any byte is written.</item>
/// <item>Only after both verifications do content write + truncate + flush happen, all through
/// the verified handle. Path strings are never re-resolved after verification.</item>
/// </list>
/// HONEST RESIDUAL: the leaf open is still path-based (not relative to the pinned parent
/// handle), so a race that redirects the OPEN itself is detected only after the fact. In that
/// lose-the-race window the elevated process may transiently hold a write handle to — or
/// create and then delete an empty file at — an attacker-chosen location. No content bytes are
/// ever written and no existing file is truncated or modified outside the verified path.
/// Closing that last sliver requires NtCreateFile with RootDirectory = the pinned parent
/// handle (no path re-resolution at all).
/// </remarks>
internal static class NoFollowFileWriter
{
    internal static Result<Unit> Write(string parentDirectory, string targetPath, byte[] content)
    {
        // --- 1. Pin the parent directory by handle (no-follow, no delete sharing). ---
        using var parentHandle = NativeFileMethods.CreateFile(
            parentDirectory,
            NativeFileMethods.FileReadAttributes,
            NativeFileMethods.FileShareRead | NativeFileMethods.FileShareWrite,
            securityAttributes: 0,
            NativeFileMethods.OpenExisting,
            NativeFileMethods.FileFlagBackupSemantics | NativeFileMethods.FileFlagOpenReparsePoint,
            templateFile: 0);

        if (parentHandle.IsInvalid)
            return Result<Unit>.Failure(ErrorKind.ElevationError,
                $"File write failed: cannot open parent directory (Win32 error {Marshal.GetLastPInvokeError()})");

        var parentCheck = VerifyHandle(parentHandle, parentDirectory,
            "An ancestor directory is a symbolic link or junction and cannot be written through");
        if (parentCheck.IsFailure)
            return parentCheck;

        // --- 2. Open the target no-follow, without truncating anything at open time. ---
        using var fileHandle = NativeFileMethods.CreateFile(
            targetPath,
            // DELETE is requested so a file created by this call can be removed via the same
            // handle if post-open verification rejects it.
            NativeFileMethods.GenericWrite | NativeFileMethods.FileReadAttributes | NativeFileMethods.Delete,
            shareMode: 0,
            securityAttributes: 0,
            NativeFileMethods.OpenAlways,
            NativeFileMethods.FileFlagOpenReparsePoint,
            templateFile: 0);
        var createError = Marshal.GetLastPInvokeError();

        if (fileHandle.IsInvalid)
            return Result<Unit>.Failure(ErrorKind.ElevationError,
                $"File write failed: cannot open target file (Win32 error {createError})");

        // OPEN_ALWAYS sets ERROR_ALREADY_EXISTS when the file pre-existed; anything else
        // means this call created it and may safely delete it again on rejection.
        var createdByThisCall = createError != NativeFileMethods.ErrorAlreadyExists;

        var targetCheck = VerifyHandle(fileHandle, targetPath,
            "Target file is a symbolic link and cannot be written to");
        if (targetCheck.IsFailure)
        {
            if (createdByThisCall)
                DeleteViaHandle(fileHandle);
            return targetCheck;
        }

        // --- 3. Write through the verified handle only. ---
        using var stream = new FileStream(fileHandle, FileAccess.Write);
        stream.Write(content);
        stream.SetLength(content.Length); // Overwrite semantics: drop any trailing old bytes.
        stream.Flush(flushToDisk: true);
        return Unit.Value;
    }

    /// <summary>
    /// Verifies an already-opened no-follow handle: it must not be a reparse point, and its
    /// true (final) path must equal <paramref name="expectedPath"/>. The reparse check catches
    /// a link at the final component (opened as itself, path unchanged); the final-path check
    /// catches resolution THROUGH a reparse point at any earlier component.
    /// </summary>
    private static Result<Unit> VerifyHandle(SafeFileHandle handle, string expectedPath, string reparseMessage)
    {
        if (!NativeFileMethods.GetFileInformationByHandleEx(
                handle,
                NativeFileMethods.FileAttributeTagInfoClass,
                out var info,
                (uint)Marshal.SizeOf<NativeFileMethods.FileAttributeTagInfo>()))
            return Result<Unit>.Failure(ErrorKind.ElevationError,
                $"File write failed: cannot query handle attributes (Win32 error {Marshal.GetLastPInvokeError()})");

        if ((info.FileAttributes & NativeFileMethods.FileAttributeReparsePoint) != 0)
            return Result<Unit>.Failure(ErrorKind.SecurityError, reparseMessage);

        var finalPath = GetFinalPath(handle);
        if (finalPath is null)
            return Result<Unit>.Failure(ErrorKind.ElevationError,
                "File write failed: cannot resolve the opened handle's final path");

        if (!FinalPathMatchesExpected(finalPath, expectedPath))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "Path resolution traversed a symbolic link or junction and was rejected");

        return Unit.Value;
    }

    /// <summary>
    /// Compares the handle's true final path against the caller-expected path. The final path
    /// is always in long canonical form while the expected path may contain 8.3 short
    /// components (e.g. <c>PROGRA~1</c>), so on a raw mismatch the expected path is converted
    /// via <c>GetLongPathName</c> and compared again. That conversion only substitutes each
    /// component with the alternate NAME of the same directory entry — it never resolves a
    /// reparse point to its target — so a genuinely redirected path still mismatches and is
    /// rejected; a conversion failure keeps the raw-compare (fail-closed) result.
    /// </summary>
    private static bool FinalPathMatchesExpected(string finalPath, string expectedPath)
    {
        var trimmedFinal = Path.TrimEndingDirectorySeparator(finalPath);
        if (string.Equals(trimmedFinal,
                Path.TrimEndingDirectorySeparator(expectedPath),
                StringComparison.OrdinalIgnoreCase))
            return true;

        var longExpected = GetLongForm(expectedPath);
        return longExpected is not null &&
               string.Equals(trimmedFinal,
                   Path.TrimEndingDirectorySeparator(longExpected),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLongForm(string path)
    {
        // Same buffer strategy as GetFinalPath: fast path, then retry with the required size.
        Span<char> buffer = stackalloc char[512];
        var utf16 = MemoryMarshal.Cast<char, ushort>(buffer);
        var length = NativeFileMethods.GetLongPathName(
            path, ref MemoryMarshal.GetReference(utf16), (uint)buffer.Length);
        if (length == 0)
            return null;
        if (length <= buffer.Length)
            return new string(buffer[..(int)length]);

        var rented = ArrayPool<char>.Shared.Rent((int)length);
        try
        {
            var span = rented.AsSpan();
            var rentedUtf16 = MemoryMarshal.Cast<char, ushort>(span);
            var retryLength = NativeFileMethods.GetLongPathName(
                path, ref MemoryMarshal.GetReference(rentedUtf16), (uint)span.Length);
            if (retryLength == 0 || retryLength > span.Length)
                return null;
            return new string(span[..(int)retryLength]);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static string? GetFinalPath(SafeFileHandle handle)
    {
        // MAX_PATH-sized fast path; retry with the reported required size for longer paths.
        // The native signature takes ushort to stay blittable; reinterpret the char buffer.
        Span<char> buffer = stackalloc char[512];
        var utf16 = MemoryMarshal.Cast<char, ushort>(buffer);
        var length = NativeFileMethods.GetFinalPathNameByHandle(
            handle, ref MemoryMarshal.GetReference(utf16), (uint)buffer.Length, 0);
        if (length == 0)
            return null;
        if (length <= buffer.Length)
            return StripDosDevicePrefix(buffer[..(int)length]);

        var rented = ArrayPool<char>.Shared.Rent((int)length);
        try
        {
            var span = rented.AsSpan();
            var rentedUtf16 = MemoryMarshal.Cast<char, ushort>(span);
            var retryLength = NativeFileMethods.GetFinalPathNameByHandle(
                handle, ref MemoryMarshal.GetReference(rentedUtf16), (uint)span.Length, 0);
            if (retryLength == 0 || retryLength > span.Length)
                return null;
            return StripDosDevicePrefix(span[..(int)retryLength]);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static string StripDosDevicePrefix(ReadOnlySpan<char> path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return string.Concat(@"\\", path[8..]);
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return new string(path[4..]);
        return new string(path);
    }

    private static void DeleteViaHandle(SafeFileHandle handle)
    {
        // Best-effort removal of a file THIS call created before verification failed. Acting
        // via the handle (not the path) guarantees exactly the created file is deleted.
        var disposition = new NativeFileMethods.FileDispositionInfo { DeleteFile = 1 };
        _ = NativeFileMethods.SetFileInformationByHandle(
            handle,
            NativeFileMethods.FileDispositionInfoClass,
            in disposition,
            (uint)Marshal.SizeOf<NativeFileMethods.FileDispositionInfo>());
    }
}
