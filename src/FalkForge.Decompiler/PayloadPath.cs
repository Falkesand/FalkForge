namespace FalkForge.Decompiler;

/// <summary>
/// Single source of truth for the relative <c>payload/</c> key used to correlate
/// a payload file's bytes (extracted from the MSI cabinet) with the
/// <c>files.Add("...")</c> argument emitted into the generated <c>Program.cs</c>.
///
/// <para>
/// Both producers MUST derive their key from this method so the generated code
/// and the extracted bytes align by construction:
/// </para>
/// <list type="bullet">
///   <item>
///     The emitter passes <see cref="FalkForge.InstallPath.Segments"/> (the install
///     subdir chain, excluding the root known folder) and the file name.
///   </item>
///   <item>
///     The payload extractor passes <c>DirectoryResolver.FindRootFolder(dirId).RelativePath</c>
///     split on <c>'/'</c> (the identical root-excluded segment chain) and the long file name.
///   </item>
/// </list>
/// </summary>
public static class PayloadPath
{
    /// <summary>
    /// Builds the relative payload key for a file.
    /// </summary>
    /// <param name="segments">
    /// Install subdir chain EXCLUDING the root known folder (e.g.
    /// <see cref="FalkForge.InstallPath.Segments"/>). Empty for a root-level file.
    /// </param>
    /// <param name="fileName">The file's long name.</param>
    /// <returns>
    /// <c>"payload/&lt;seg&gt;/&lt;seg&gt;/&lt;fileName&gt;"</c> with forward slashes;
    /// a root-level file yields <c>"payload/&lt;fileName&gt;"</c>.
    /// </returns>
    public static string For(IReadOnlyList<string> segments, string fileName)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(fileName);

        if (segments.Count == 0)
            return $"payload/{fileName}";

        return $"payload/{string.Join('/', segments)}/{fileName}";
    }

    /// <summary>
    /// Deterministically assigns a UNIQUE payload key for every file even when two files
    /// install to the same directory under the same name (legal in MSI: distinct File rows
    /// may share a FileName within one directory). The single source of truth for collision
    /// disambiguation: both the byte extractor and the C# emitter create one of these and
    /// feed it files in File-table order, so each side derives identical unique keys.
    ///
    /// <para>
    /// The first file to claim a base key keeps the unqualified <c>payload/&lt;dir&gt;/&lt;name&gt;</c>
    /// form; each later collision is qualified with a 1-based occurrence index inserted before
    /// the file name (<c>payload/&lt;dir&gt;/&lt;name&gt;__2/&lt;name&gt;</c>), guaranteeing both
    /// uniqueness and a stable, order-driven mapping shared by both producers.
    /// </para>
    /// </summary>
    public sealed class Deduplicator
    {
        // base key → number of files already assigned to it (in File-table order).
        private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

        /// <summary>
        /// Returns the unique payload key for the next file at <paramref name="segments"/>
        /// with <paramref name="fileName"/>, qualifying duplicates deterministically.
        /// </summary>
        public string Next(IReadOnlyList<string> segments, string fileName)
        {
            ArgumentNullException.ThrowIfNull(segments);
            ArgumentNullException.ThrowIfNull(fileName);

            var baseKey = For(segments, fileName);
            var count = _seen.TryGetValue(baseKey, out var c) ? c : 0;
            _seen[baseKey] = count + 1;

            if (count == 0)
                return baseKey;

            // Insert a per-occurrence subdirectory before the file name so the qualified
            // key stays a valid relative path and the file name on disk is preserved.
            var occurrence = count + 1;
            var slash = baseKey.LastIndexOf('/');
            var prefix = baseKey[..slash];
            var name = baseKey[(slash + 1)..];
            return $"{prefix}/{name}__{occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{name}";
        }
    }
}
