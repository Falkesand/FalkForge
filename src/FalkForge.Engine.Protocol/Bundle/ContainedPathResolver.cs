using System.Diagnostics.CodeAnalysis;

namespace FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Path-containment check for the bundle extraction choke point
/// (<see cref="BundleReader.ExtractPayloadToFile(string, TocEntry, string, string)"/>). TOC
/// <c>PackageId</c> strings are attacker-controlled in a crafted bundle, so any write destination
/// derived from one must be proven to stay inside the intended extraction directory (path
/// traversal / zip-slip, OWASP A03: Injection).
/// <para>
/// This is a deliberate near-duplicate of <c>FalkForge.Cli.Security.ContainedPathResolver</c>:
/// the CLI helper is internal to that assembly, and this AOT-safe protocol assembly must not
/// depend on CLI code, so each layer carries its own copy rather than sharing a third one.
/// Keep the two in sync when the containment rules change.
/// </para>
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
            // from a crafted TOC — reject as non-contained instead of crashing the engine.
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
