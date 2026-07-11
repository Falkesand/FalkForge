using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Internal shared helpers used across multiple table producers.
/// Factored here to eliminate copy-paste and keep individual producer files
/// focused on their own table logic.
/// </summary>
internal static class ProducerHelpers
{
    // Well-known MSI system directory IDs referenced by shortcuts. These are
    // virtual directories whose real paths are resolved by the installer at
    // run time; they must appear in the Directory table with TARGETDIR as
    // parent and "." as DefaultDir.
    internal const string ProgramMenuFolderId = "ProgramMenuFolder";
    internal const string DesktopFolderId = "DesktopFolder";
    internal const string StartupFolderId = "StartupFolder";

    /// <summary>
    /// Builds a filename-to-component lookup dictionary from the resolved component list.
    /// The mapping is case-insensitive (Windows file-system convention) and uses
    /// first-match-wins semantics to mirror the legacy <c>EmitAssemblies</c> resolution.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="MsiAssemblyTableProducer"/> and
    /// <see cref="MsiAssemblyNameTableProducer"/>; both need identical resolution logic
    /// so callers that invoke both producers in the same build should prefer the cached
    /// version on <see cref="RecipeBuildContext"/> if available — see
    /// <see cref="RecipeBuildContext.GetOrBuildFileToComponentMap"/> for the per-build cache.
    /// </remarks>
    /// <param name="components">Resolved component list from <see cref="ResolvedPackage"/>.</param>
    /// <returns>A new <see cref="Dictionary{TKey,TValue}"/> keyed by file name.</returns>
    internal static Dictionary<string, ResolvedComponent> BuildFileToComponentMap(
        IReadOnlyList<ResolvedComponent> components)
    {
        Dictionary<string, ResolvedComponent> map =
            new(components.Count, StringComparer.OrdinalIgnoreCase);

        foreach (ResolvedComponent comp in components)
        {
            foreach (ResolvedFile file in comp.Files)
            {
                map.TryAdd(file.FileName, comp);
            }
        }

        return map;
    }

    /// <summary>
    /// Computes the deterministic Directory row ID for a Start Menu subfolder.
    /// The ID is <c>SM_{sanitized}_{stableHash4}</c>, truncated to 72 characters
    /// if necessary. Mirrors the algorithm in <c>TableEmitter.GetStartMenuSubfolderId</c>.
    /// </summary>
    internal static string GetStartMenuSubfolderId(string subfolder)
    {
        string id = string.Create(
            CultureInfo.InvariantCulture,
            $"SM_{SanitizeDirectoryId(subfolder)}_{StableHash4(subfolder)}");
        return id.Length > 72 ? id[..72] : id;
    }

    /// <summary>
    /// Replaces every character that is not a letter, digit, underscore, or dot
    /// with an underscore — matching the identifier sanitisation in
    /// <c>TableEmitter.SanitizeId</c>.
    /// </summary>
    internal static string SanitizeDirectoryId(string name)
    {
        // Avoid allocation for the common case where no replacement is needed.
        bool needsReplacement = false;
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '.'))
            {
                needsReplacement = true;
                break;
            }
        }

        if (!needsReplacement)
            return name;

        char[] buf = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            buf[i] = char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_';
        }

        return new string(buf);
    }

    /// <summary>
    /// Returns the first 4 bytes of the SHA-256 hash of <paramref name="input"/>
    /// encoded as 8 upper-case hex characters. Used as a stable disambiguation
    /// suffix for generated Directory IDs.
    /// </summary>
    internal static string StableHash4(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 4);
    }

    /// <summary>
    /// Computes the deterministic MSI <c>Icon.Name</c> primary key for an icon
    /// source file path. The name is <c>Icon_{stableHash8}{extension}</c>: the
    /// <c>Icon_</c> prefix guarantees a valid MSI identifier start (letter),
    /// the hash of the path makes it deterministic and dedup-stable (identical
    /// source path ⇒ identical name ⇒ a single Icon row shared by all
    /// consumers), and the file extension is preserved because MSI convention
    /// wants the Icon.Name to carry an extension-like suffix (e.g. <c>.exe</c>
    /// or <c>.ico</c>) so the shell picks the right icon renderer. Every
    /// character is a letter, digit, underscore, or dot, so the result is a
    /// well-formed MSI identifier within the 72-char column width.
    /// </summary>
    internal static string ResolveIconName(string iconFilePath)
    {
        // Sanitize the extension: keep the leading dot, drop any non-alnum
        // characters after it, lower-case for stability. Fall back to ".ico"
        // when the source has no usable extension.
        string rawExt = Path.GetExtension(iconFilePath);
        string suffix = ".ico";
        if (!string.IsNullOrEmpty(rawExt) && rawExt.Length > 1)
        {
            char[] buf = new char[rawExt.Length];
            int len = 0;
            buf[len++] = '.';
            for (int i = 1; i < rawExt.Length; i++)
            {
                char c = char.ToLowerInvariant(rawExt[i]);
                if (char.IsLetterOrDigit(c))
                {
                    buf[len++] = c;
                }
            }

            if (len > 1)
            {
                suffix = new string(buf, 0, len);
            }
        }

        string name = string.Concat("Icon_", StableHash4Wide(iconFilePath), suffix);

        // Cap at the Icon.Name CHAR(72) column width, matching the truncation
        // convention of the sibling identifier generators (e.g.
        // GetStartMenuSubfolderId). The 21-char "Icon_" + 16-hex prefix is
        // hash-unique on its own, so a truncated suffix cannot collide two
        // distinct sources that did not already collide on the hash.
        return name.Length > 72 ? name[..72] : name;
    }

    /// <summary>
    /// Returns the first 8 bytes of the SHA-256 hash of <paramref name="input"/>
    /// as 16 upper-case hex characters — a wider variant of
    /// <see cref="StableHash4"/> used to keep Icon.Name collisions negligible
    /// across distinct source paths.
    /// </summary>
    internal static string StableHash4Wide(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 8);
    }
}
