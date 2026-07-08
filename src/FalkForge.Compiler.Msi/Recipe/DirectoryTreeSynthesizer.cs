using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Msi.Tables;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Computes deterministic MSI <c>Directory</c> table primary keys from
/// <see cref="InstallPath"/> values, mirroring the synthesis rules used by
/// <c>TableEmitter.EmitDirectories</c>. Producers consume these IDs to keep
/// recipe-driven Directory rows and Component <c>Directory_</c> foreign keys
/// in lockstep with the legacy emitter, which is the contract every existing
/// integration test depends on.
/// </summary>
/// <remarks>
/// Stays inside the recipe namespace because all callers — Directory and
/// Component producers — already live here and we do not want to widen the
/// surface area of <see cref="FalkForge.Compiler.Msi.Tables.WellKnownDirectoryIds"/>.
/// </remarks>
internal static class DirectoryTreeSynthesizer
{
    /// <summary>
    /// Computes the synthesized Directory primary key for <paramref name="path"/>.
    /// When the path coincides with the configured install directory leaf, that
    /// leaf is materialized under <see cref="WellKnownDirectoryIds.InstallDir"/>
    /// rather than as a generated <c>D_*</c> identifier — required because the
    /// MSI Formatted evaluator only resolves the literal token "INSTALLDIR".
    /// </summary>
    internal static string ComputeDirectoryId(InstallPath path, InstallPath? installDir)
    {
        IReadOnlyList<string> segments = path.Segments;
        if (segments.Count == 0)
        {
            return path.Root.Token;
        }

        IReadOnlyList<string>? installSegments = installDir?.Segments;
        bool sameRoot = installDir is not null && path.Root.Token == installDir.Root.Token;

        string parentId = path.Root.Token;
        for (int i = 0; i < segments.Count; i++)
        {
            bool atInstallLeaf =
                sameRoot &&
                installSegments is not null &&
                installSegments.Count > 0 &&
                i == installSegments.Count - 1 &&
                SegmentsMatch(segments, installSegments, installSegments.Count);

            if (atInstallLeaf)
            {
                parentId = WellKnownDirectoryIds.InstallDir;
            }
            else
            {
                parentId = $"D_{SanitizeId(segments[i])}_{StableHash(parentId)}";
                if (parentId.Length > 72)
                {
                    parentId = parentId[..72];
                }
            }
        }

        return parentId;
    }

    /// <summary>
    /// Returns an <see cref="InstallPath"/> rooted at the same KnownFolder as
    /// <paramref name="path"/> whose RelativePath covers only the first
    /// <paramref name="count"/> segments. Used by the directory tree walk to
    /// re-ask <see cref="ComputeDirectoryId"/> what ID a partial prefix gets,
    /// avoiding a duplicate hash chain inline.
    /// </summary>
    internal static InstallPath BuildPrefixPath(InstallPath path, int count)
    {
        IReadOnlyList<string> segments = path.Segments;
        if (count >= segments.Count)
        {
            return path;
        }

        if (count <= 0)
        {
            return path.Root / string.Empty;
        }

        InstallPath prefix = path.Root / segments[0];
        for (int i = 1; i < count; i++)
        {
            prefix /= segments[i];
        }

        return prefix;
    }

    private static bool SegmentsMatch(IReadOnlyList<string> a, IReadOnlyList<string> b, int count)
    {
        if (a.Count < count || b.Count < count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string SanitizeId(string name)
    {
        // Avoid allocation for the common case where no replacement is needed.
        bool needsReplacement = false;
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            {
                needsReplacement = true;
                break;
            }
        }

        if (!needsReplacement)
        {
            return name;
        }

        char[] sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }

        return new string(sanitized);
    }

    private static string StableHash(string input)
    {
        // Match TableEmitter exactly: SHA-256 → first 4 bytes → 8 hex chars.
        // Deterministic across runtimes, used as the disambiguator suffix on
        // every D_* directory identifier. Diverging from this hashing scheme
        // would split the recipe pipeline from the legacy emitter and break
        // every byte-diff round-trip test in the phase 9 harness.
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 4);
    }
}
