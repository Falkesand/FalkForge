namespace FalkForge.Engine.Elevation.Commands;

/// <summary>
/// Shared path-safety helpers for elevated filesystem commands that run as SYSTEM.
/// Each command selects its OWN set of allowed roots via <see cref="FileWriteRoots"/> or
/// <see cref="ServiceBinaryRoots"/>; no single allowlist silently governs multiple commands.
/// </summary>
internal static class ElevatedPathPolicy
{
    /// <summary>
    /// Allowed roots for arbitrary elevated file writes. Includes the user profile because
    /// per-user application data under the profile is a legitimate write target.
    /// </summary>
    internal static string[] FileWriteRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    ];

    /// <summary>
    /// Allowed roots for a SYSTEM service's binary image. Deliberately EXCLUDES the user
    /// profile: a service image under a user-writable directory is a weak-service-path
    /// privilege escalation (the user can swap the binary and inherit SYSTEM).
    /// </summary>
    internal static string[] ServiceBinaryRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="normalizedPath"/> equals, or is
    /// contained under, any of <paramref name="allowedRoots"/>. Containment requires a
    /// directory-separator boundary so a sibling directory that merely shares a textual
    /// prefix (e.g. <c>C:\Program Files Evil</c> vs <c>C:\Program Files</c>) is NOT matched.
    /// </summary>
    internal static bool IsUnderAllowedRoot(string normalizedPath, ReadOnlySpan<string> allowedRoots)
    {
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            // Exact-root equality is allowed (writing directly into the root).
            if (normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;

            // Otherwise require a separator boundary to defeat the sibling-prefix hole.
            var rootWithSeparator = root[^1] == Path.DirectorySeparatorChar
                ? root
                : root + Path.DirectorySeparatorChar;

            if (normalizedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Ensures the full directory tree from the matched allowed root down to
    /// <paramref name="leafDirectory"/> is free of reparse points (junctions / symbolic
    /// links), creating any missing level itself so a freshly created directory cannot be
    /// a junction. Rejects when ANY existing ancestor OR the leaf is a reparse point.
    /// </summary>
    /// <remarks>
    /// This closes the STATIC ancestor-junction pre-plant: <see cref="Directory.CreateDirectory(string)"/>
    /// will walk THROUGH an existing ancestor junction, so an attacker who pre-creates a
    /// junction under a writable allowed root (e.g. ProgramData) could redirect an elevated
    /// write into a forbidden location such as System32. Verifying every existing level and
    /// creating every missing level one at a time defeats a junction planted BEFORE the check.
    /// A path-based TOCTOU residual remains: the subsequent write is path-based (e.g.
    /// <see cref="File.WriteAllBytes(string, byte[])"/>), not a held no-follow handle, so a
    /// junction swapped in between this verification and the write is not detected — the
    /// handle-based no-follow write is tracked as a follow-up.
    /// </remarks>
    internal static Result<Unit> EnsureDirectoryTreeSafe(string leafDirectory, ReadOnlySpan<string> allowedRoots)
    {
        // Identify which allowed root contains the leaf so the walk has a fixed, trusted anchor.
        string? matchedRoot = null;
        foreach (var root in allowedRoots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            if (leafDirectory.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                matchedRoot = root;
                break;
            }

            var rootWithSeparator = root[^1] == Path.DirectorySeparatorChar
                ? root
                : root + Path.DirectorySeparatorChar;

            if (leafDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                matchedRoot = root;
                break;
            }
        }

        if (matchedRoot is null)
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "Target directory is outside allowed directories");

        // The anchor root itself must not be a reparse point.
        if (IsReparsePoint(matchedRoot))
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                "An ancestor directory is a symbolic link or junction and cannot be written through");

        var relative = Path.GetRelativePath(matchedRoot, leafDirectory);
        if (relative is "." or "")
            return Unit.Value; // leaf == root, nothing to descend into.

        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = matchedRoot;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);

            if (Directory.Exists(current))
            {
                if (IsReparsePoint(current))
                    return Result<Unit>.Failure(ErrorKind.SecurityError,
                        "An ancestor directory is a symbolic link or junction and cannot be written through");
            }
            else
            {
                // Create exactly this level. Its parent already exists and was verified above,
                // so CreateDirectory does not walk through any unverified junction.
                Directory.CreateDirectory(current);
            }
        }

        return Unit.Value;
    }

    private static bool IsReparsePoint(string directory)
    {
        var info = new DirectoryInfo(directory);
        return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
