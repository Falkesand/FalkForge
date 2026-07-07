using System.Diagnostics.CodeAnalysis;

namespace FalkForge;

/// <summary>
/// Path-containment check used wherever a file is written to a location named by untrusted input —
/// an MSI Directory/File table entry, a bundle TOC <c>PackageId</c>, or a migration payload key.
/// A crafted <c>..\..\</c> segment (or an absolute path) in any of those must never let a write
/// escape the caller's output directory (path traversal / zip-slip, OWASP A03: Injection).
/// This is the single shared implementation used across Cli and Engine.Protocol.
/// <para>
/// Callers follow two deliberate conventions on rejection: <c>MsiExtractor</c> fails the whole
/// extraction loud on the first escape attempt (a hostile MSI is rejected wholesale), while
/// <c>MigrateCommand</c> and <c>ExtractCommand</c>'s bundle path skip-and-report each hostile
/// entry and exit non-zero (multi-entry outputs where the safe entries are still useful). Pick
/// one of these two for new callers; do not invent a third.
/// </para>
/// </summary>
public static class ContainedPathResolver
{
    /// <summary>
    /// Resolves <paramref name="relativeKey"/> relative to <paramref name="baseDir"/> and
    /// verifies the result stays strictly inside that directory.
    /// Uses <see cref="Path.GetRelativePath(string, string)"/> to perform an OS-correct
    /// containment check that does not rely on string case comparison.
    /// Returns <see langword="false"/> when the resolved path is rooted, equals <c>..</c>,
    /// or starts with <c>../</c> (or <c>..\</c> on Windows) — this also rejects the case where
    /// <paramref name="relativeKey"/> is itself an absolute path pointing outside
    /// <paramref name="baseDir"/>. Illegal path input (embedded NUL, pathologically long) is
    /// treated as non-contained rather than allowed to throw — hostile input must produce a
    /// graceful reject, never a crash.
    /// </summary>
    public static bool TryResolveContained(string baseDir, string relativeKey, [NotNullWhen(true)] out string? fullPath)
    {
        string rel;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(baseDir, relativeKey));
            rel = Path.GetRelativePath(baseDir, fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            // Embedded NUL (ArgumentException) or an absurdly long key (PathTooLongException)
            // from a crafted input — reject as non-contained instead of crashing the caller.
            fullPath = null;
            return false;
        }

        if (Path.IsPathRooted(rel) ||
            rel == ".." ||
            rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            rel.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            fullPath = null;
            return false;
        }

        return true;
    }
}
