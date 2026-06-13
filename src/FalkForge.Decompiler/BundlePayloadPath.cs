using FalkForge.Compiler.Bundle;

namespace FalkForge.Decompiler;

/// <summary>
/// Single source of truth for the relative payload key (under a "payload/" prefix) used to
/// correlate a bundle chain package's bytes (extracted from the bundle's embedded payloads)
/// with the package path emitted into the generated Program.cs
/// (e.g. c.MsiPackage("payload/name")).
///
/// <para>
/// The bundle analogue of <see cref="PayloadPath"/>. The migration generator builds a single
/// packageId-to-payload-key map via <see cref="For"/> and uses it for BOTH the emitted package
/// path (through the emitter's package-path resolver) AND the MigrationResult.Payloads byte-map
/// key, so the two align by construction.
/// </para>
/// </summary>
public static class BundlePayloadPath
{
    private const string Prefix = "payload/";

    /// <summary>
    /// Builds the relative payload key for a bundle chain package from its source path's
    /// file name: "payload/&lt;fileName&gt;" with a forward slash.
    /// </summary>
    /// <param name="sourcePath">
    /// The package's original source path (may be an absolute path, a bare file name, or empty).
    /// Only the file-name component is used.
    /// </param>
    public static string For(string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        // Bundle source paths may use '\' or '/'; normalise so file-name extraction is
        // deterministic across platforms.
        var normalised = sourcePath.Replace('\\', '/');
        var slash = normalised.LastIndexOf('/');
        var fileName = slash < 0 ? normalised : normalised.Substring(slash + 1);

        return Prefix + fileName;
    }

    /// <summary>
    /// Builds a deterministic packageId-to-payload-key map for every package model,
    /// disambiguating duplicate file names by prefixing the colliding package's id:
    /// "payload/&lt;packageId&gt;/&lt;fileName&gt;". The first package to claim a given key
    /// keeps the unqualified form; later collisions are qualified.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildMap(IEnumerable<BundlePackageModel> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var package in packages)
        {
            if (map.ContainsKey(package.Id))
                continue;

            var key = For(package.SourcePath);
            if (!claimed.Add(key))
            {
                var fileName = key.Substring(Prefix.Length);
                key = Prefix + package.Id + "/" + fileName;
                claimed.Add(key);
            }

            map[package.Id] = key;
        }

        return map;
    }
}
