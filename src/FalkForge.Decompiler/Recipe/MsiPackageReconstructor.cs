using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Diagnostics;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Pure cross-platform stage that converts raw schema rows (produced by
/// <see cref="TableReadEngine"/>) into a <see cref="PackageModel"/>.
/// Zero <see cref="IMsiTableAccess"/> touches — runs on any OS without msi.dll.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static readonly Version DefaultVersion = new(1, 0, 0);

    /// <summary>msidbFileAttributesVital — File.Attributes bit that marks a file vital.</summary>
    private const int FileAttributesVital = 512;

    /// <summary>
    /// Extracts package-level metadata (name, version, manufacturer, codes, scope)
    /// from a <see cref="PropertySet"/>. Used both by the full
    /// <see cref="Rebuild"/> path and by isolated unit tests.
    /// </summary>
    public static PackageMetadata ExtractMetadata(PropertySet props)
    {
        var name = props.GetOrDefault("ProductName", "Unknown");
        var manufacturer = props.GetOrDefault("Manufacturer", "Unknown");
        var versionStr = props.GetOrDefault("ProductVersion", "1.0.0");

        Version.TryParse(versionStr, out var version);
        version ??= DefaultVersion;

        Guid.TryParse(props.Get("UpgradeCode"), out var upgradeCode);
        Guid.TryParse(props.Get("ProductCode"), out var productCode);

        var scope = InstallScope.PerMachine;
        var allUsers = props.Get("ALLUSERS");
        // Only switch to PerUser when ALLUSERS is explicitly present and is "2" or empty string.
        // Absent key keeps the default PerMachine.
        if (allUsers is not null && (allUsers == "2" || allUsers.Length == 0))
            scope = InstallScope.PerUser;

        return new PackageMetadata
        {
            Name = name,
            Manufacturer = manufacturer,
            Version = version,
            UpgradeCode = upgradeCode,
            ProductCode = productCode,
            Scope = scope,
        };
    }

    /// <summary>
    /// Rebuilds a <see cref="PackageModel"/> from the row collections produced
    /// by reading each table schema via <see cref="TableReadEngine.ReadOne{TRow}"/>.
    /// All parameters come from the read pipeline; this method performs no IO and never fails.
    /// </summary>
    /// <param name="logger">
    /// Optional structured logger. Defaults to <see langword="null"/> (no-op) so every existing
    /// caller behaves unchanged. When supplied, a <c>Debug</c> entry summarising the reconstructed
    /// model (feature/file/registry/service/shortcut counts) is logged before returning.
    /// </param>
    public static PackageModel Rebuild(
        IReadOnlyList<PropertyRow>          propertyRows,
        IReadOnlyList<DirectoryRow>         directoryRows,
        IReadOnlyList<ComponentRow>         componentRows,
        IReadOnlyList<FileRow>              fileRows,
        IReadOnlyList<FeatureRow>           featureRows,
        IReadOnlyList<FeatureComponentsRow> featureComponentsRows,
        IReadOnlyList<RegistryRow>          registryRows,
        IReadOnlyList<ServiceRow>           serviceRows,
        IReadOnlyList<ShortcutRow>          shortcutRows,
        IReadOnlyList<UpgradeRow>           upgradeRows,
        IFalkLogger?                        logger = null)
    {
        var props = PropertySet.From(propertyRows);
        var meta = ExtractMetadata(props);

        // Build directory resolver from raw rows
        var dirEntries = directoryRows
            .Select(r => new DirectoryEntry
            {
                DirectoryId = r.Directory,
                ParentDirectoryId = string.IsNullOrEmpty(r.Directory_Parent) ? null : r.Directory_Parent,
                DefaultDir = r.DefaultDir
            })
            .ToList();
        var dirResolver = new DirectoryResolver(dirEntries);

        // Component-to-directory map
        var componentDirMap = componentRows.ToDictionary(
            c => c.Component, c => c.Directory_, StringComparer.Ordinal);

        // Component-to-condition map
        var componentCondMap = componentRows.ToDictionary(
            c => c.Component, c => c.Condition, StringComparer.Ordinal);

        // Component key-path map
        var componentKeyMap = componentRows.ToDictionary(
            c => c.Component, c => c.KeyPath, StringComparer.Ordinal);

        var fileEntries = BuildFileEntries(fileRows, componentDirMap, componentKeyMap, componentCondMap, dirResolver);
        var features = BuildFeatures(featureRows, featureComponentsRows);
        var registryEntries = BuildRegistryEntries(registryRows);
        var services = BuildServices(serviceRows);
        var shortcuts = BuildShortcuts(shortcutRows);
        var (majorUpgrade, downgrade) = ReconstructUpgrade(upgradeRows);
        var userProps = BuildUserProperties(propertyRows);
        var defaultInstallDir = ResolveDefaultInstallDirectory(directoryRows, dirResolver);

        if (logger is not null && logger.MinimumLevel <= LogLevel.Debug)
        {
            logger.Debug("MsiDecompiler",
                $"Reconstructed package model: {features.Count} feature(s), {fileEntries.Count} file(s), " +
                $"{registryEntries.Count} registry entrie(s), {services.Count} service(s), {shortcuts.Count} shortcut(s).");
        }

        return new PackageModel
        {
            Name = meta.Name,
            Manufacturer = meta.Manufacturer,
            Version = meta.Version,
            UpgradeCode = meta.UpgradeCode,
            ProductCode = meta.ProductCode,
            Scope = meta.Scope,
            DefaultInstallDirectory = defaultInstallDir,
            Files = fileEntries,
            Features = features,
            RegistryEntries = registryEntries,
            Services = services,
            Shortcuts = shortcuts,
            Properties = userProps,
            MajorUpgrade = majorUpgrade,
            Downgrade = downgrade
        };
    }
}
