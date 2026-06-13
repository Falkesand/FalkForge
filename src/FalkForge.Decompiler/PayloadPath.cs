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
}
