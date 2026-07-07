using System.Diagnostics.CodeAnalysis;

namespace FalkForge.Cli.Security;

/// <summary>
/// Shared path-containment check used by every CLI command that writes files at a location named
/// by an untrusted input — an MSI Directory/File table entry, a bundle TOC <c>PackageId</c>, or a
/// migration payload key. A crafted <c>..\..\</c> segment (or an absolute path) in any of those
/// must never let a write escape the caller's output directory (path traversal / zip-slip, OWASP
/// A03: Injection).
/// </summary>
internal static class ContainedPathResolver
{
    /// <summary>
    /// Resolves <paramref name="relativeKey"/> relative to <paramref name="baseDir"/> and
    /// verifies the result stays strictly inside that directory.
    /// Uses <see cref="Path.GetRelativePath(string, string)"/> to perform an OS-correct
    /// containment check that does not rely on string case comparison.
    /// Returns <see langword="false"/> when the resolved path is rooted, equals <c>..</c>,
    /// or starts with <c>../</c> (or <c>..\</c> on Windows) — this also rejects the case where
    /// <paramref name="relativeKey"/> is itself an absolute path pointing outside
    /// <paramref name="baseDir"/>.
    /// </summary>
    public static bool TryResolveContained(string baseDir, string relativeKey, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(baseDir, relativeKey));
        var rel = Path.GetRelativePath(baseDir, fullPath);

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
