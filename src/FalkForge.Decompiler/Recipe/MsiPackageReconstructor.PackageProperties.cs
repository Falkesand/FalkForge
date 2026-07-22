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
        var secureNames = propertyRows
            .FirstOrDefault(p => p.Property == "SecureCustomProperties")
            ?.Value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        // User-defined properties (non-internal)
        var internalProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ProductCode", "ProductName", "ProductVersion", "Manufacturer",
            "UpgradeCode", "ProductLanguage", "ALLUSERS", "ARPNOMODIFY",
            "ARPNOREPAIR", "ARPNOREMOVE", "SecureCustomProperties",
            "MsiLogFileLocation", "INSTALLLEVEL", "REINSTALLMODE",
            "ROOTDRIVE", "LIMITUI", "MsiHiddenProperties"
        };
        return propertyRows
            .Where(p => !string.IsNullOrEmpty(p.Property) && !internalProps.Contains(p.Property))
            .Select(p => new PropertyModel
            {
                Name = p.Property,
                Value = p.Value,
                IsSecure = secureNames.Contains(p.Property),
                IsHidden = false
            })
            .ToList();
    }

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
