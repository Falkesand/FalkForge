using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Final <see cref="Rebuild"/>-assembly helpers: filtering user-defined
/// (non-internal) properties and resolving the default install directory from
/// well-known directory ids.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<PropertyModel> BuildUserProperties(IReadOnlyList<PropertyRow> propertyRows)
    {
        // All-uppercase names make an MSI property PUBLIC (overridable from the command line) — that
        // is a different concept from SECURE (passed through to the elevated execute sequence). Only
        // names listed in SecureCustomProperties are secure; read that value and test membership
        // instead of re-deriving "secure" from the naming convention that only proves "public".
        var secureNames = SplitPropertyList(propertyRows, "SecureCustomProperties");

        // AdminProperties (IsAdmin) and MsiHiddenProperties (IsHidden) are computed and emitted the
        // same way as SecureCustomProperties — see PropertyTableProducer / HiddenPropertiesEmitter —
        // so round-tripping them back onto PropertyModel follows the identical membership-test shape.
        var adminNames = SplitPropertyList(propertyRows, "AdminProperties");
        var hiddenNames = SplitPropertyList(propertyRows, "MsiHiddenProperties");

        // User-defined properties (non-internal)
        var internalProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ProductCode", "ProductName", "ProductVersion", "Manufacturer",
            "UpgradeCode", "ProductLanguage", "ALLUSERS", "ARPNOMODIFY",
            "ARPNOREPAIR", "ARPNOREMOVE", "SecureCustomProperties",
            "MsiLogFileLocation", "INSTALLLEVEL", "REINSTALLMODE",
            "ROOTDRIVE", "LIMITUI", "MsiHiddenProperties", "AdminProperties"
        };
        return propertyRows
            .Where(p => !string.IsNullOrEmpty(p.Property) && !internalProps.Contains(p.Property))
            .Select(p => new PropertyModel
            {
                Name = p.Property,
                Value = p.Value,
                // A mixed-case entry in SecureCustomProperties is malformed but still writable in a
                // foreign/third-party MSI (this list is never validated by Windows Installer itself).
                // SecureCustomProperties only ever recognizes public property names (no lowercase
                // letter), so a mixed-case entry is already inert in the ORIGINAL MSI -- it is never
                // actually treated as secure there either. Reconstructing IsSecure=true from it would
                // misrepresent the source; reporting IsSecure=false is the faithful read, not a lossy
                // one, and keeps `forge migrate`/`forge verify --rebuild` from hard-failing on PRP001
                // for a name the user cannot fix in someone else's MSI.
                IsSecure = secureNames.Contains(p.Property) && !ContainsLowercase(p.Property),
                IsAdmin = adminNames.Contains(p.Property),
                IsHidden = hiddenNames.Contains(p.Property)
            })
            .ToList();
    }

    private static bool ContainsLowercase(string name)
    {
        foreach (char c in name)
        {
            if (char.IsLower(c))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the semicolon-delimited value of the internal property named <paramref name="listPropertyName"/>
    /// (e.g. <c>SecureCustomProperties</c>, <c>AdminProperties</c>, <c>MsiHiddenProperties</c>) and splits
    /// it into a membership set. A name in the list has no guarantee of also appearing as its own
    /// <see cref="PropertyRow"/> — e.g. an extension's deferred-action <c>CustomActionData</c> carrier
    /// property, set only at run time — so callers must test membership rather than assume every listed
    /// name corresponds to a row.
    /// </summary>
    private static HashSet<string> SplitPropertyList(IReadOnlyList<PropertyRow> propertyRows, string listPropertyName)
        => propertyRows
            .FirstOrDefault(p => p.Property == listPropertyName)
            ?.Value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];

    private static InstallPath? ResolveDefaultInstallDirectory(
        IReadOnlyList<DirectoryRow> directoryRows,
        DirectoryResolver dirResolver)
    {
        // Default install directory from known directory IDs
        foreach (var dirName in new[] { "INSTALLFOLDER", "INSTALLDIR", "APPDIR" })
        {
            if (directoryRows.Any(d => d.Directory == dirName))
            {
                var (root, relPath) = dirResolver.FindRootFolder(dirName);
                if (root is not null)
                {
                    return root / relPath;
                }
            }
        }

        return null;
    }
}
