using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;

namespace FalkForge.Cli.Diff;

/// <summary>
/// Computes a <see cref="PlanDiffResult"/> by comparing two <see cref="MsiReadRecipe"/>
/// instances. All logic is pure (no I/O) so it can be exercised in-memory without MSI files.
/// </summary>
public static class MsiPlanDiff
{
    /// <summary>
    /// Diffs <paramref name="oldRecipe"/> against <paramref name="newRecipe"/> and returns
    /// a structured result suitable for human or machine consumption.
    /// </summary>
    public static PlanDiffResult Diff(
        string oldPath,
        string newPath,
        MsiReadRecipe oldRecipe,
        MsiReadRecipe newRecipe)
    {
        var sections = new List<PlanDiffSection>
        {
            DiffProperties(oldRecipe.Properties, newRecipe.Properties),
            DiffFeatures(oldRecipe.Features, newRecipe.Features),
            DiffFiles(oldRecipe.Files, newRecipe.Files),
            DiffServices(oldRecipe.Services, newRecipe.Services),
            DiffRegistry(oldRecipe.RegistryEntries, newRecipe.RegistryEntries),
            DiffShortcuts(oldRecipe.Shortcuts, newRecipe.Shortcuts),
            DiffUpgrades(oldRecipe.Upgrades, newRecipe.Upgrades),
        };

        // Only include sections that have at least one item (skip empty = no table).
        var nonEmpty = sections
            .Where(s => s.Items.Count > 0)
            .ToList();

        return new PlanDiffResult("msi", oldPath, newPath, nonEmpty);
    }

    // -------------------------------------------------------------------------
    // Properties (key product identity fields only — ProductCode, ProductVersion,
    // Manufacturer, UpgradeCode, ProductName, ARPCONTACT, ARPHELPLINK)
    // -------------------------------------------------------------------------
    private static readonly HashSet<string> s_trackedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProductCode", "ProductVersion", "ProductName", "Manufacturer",
        "UpgradeCode", "ARPCONTACT", "ARPHELPLINK", "ARPURLINFOABOUT",
        "INSTALLLEVEL", "ALLUSERS",
    };

    private static PlanDiffSection DiffProperties(
        IReadOnlyList<PropertyRow> oldRows,
        IReadOnlyList<PropertyRow> newRows)
    {
        var oldMap = oldRows
            .Where(r => s_trackedProperties.Contains(r.Property))
            .OrderBy(r => r.Property, StringComparer.Ordinal)
            .ToDictionary(r => r.Property, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var newMap = newRows
            .Where(r => s_trackedProperties.Contains(r.Property))
            .OrderBy(r => r.Property, StringComparer.Ordinal)
            .ToDictionary(r => r.Property, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var items = DiffMaps(oldMap, newMap);
        return new PlanDiffSection("Properties", items);
    }

    // -------------------------------------------------------------------------
    // Features
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffFeatures(
        IReadOnlyList<FeatureRow> oldRows,
        IReadOnlyList<FeatureRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(r => r.Feature, StringComparer.Ordinal)
            .ToDictionary(r => r.Feature, r => FormatFeature(r), StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(r => r.Feature, StringComparer.Ordinal)
            .ToDictionary(r => r.Feature, r => FormatFeature(r), StringComparer.Ordinal);

        return new PlanDiffSection("Features", DiffMaps(oldMap, newMap));
    }

    private static string FormatFeature(FeatureRow r) =>
        $"title={r.Title}, level={r.Level}, parent={r.Feature_Parent ?? "(root)"}";

    // -------------------------------------------------------------------------
    // Files  (keyed by logical file ID; label = long file name portion)
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffFiles(
        IReadOnlyList<FileRow> oldRows,
        IReadOnlyList<FileRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(r => r.FileName, StringComparer.Ordinal)
            .ToDictionary(r => r.File, r => FormatFile(r), StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(r => r.FileName, StringComparer.Ordinal)
            .ToDictionary(r => r.File, r => FormatFile(r), StringComparer.Ordinal);

        // Use the long-name portion as label for readability.
        var oldNames = oldRows.ToDictionary(r => r.File, r => LongName(r.FileName), StringComparer.Ordinal);
        var newNames = newRows.ToDictionary(r => r.File, r => LongName(r.FileName), StringComparer.Ordinal);

        return new PlanDiffSection("Files", DiffMaps(oldMap, newMap, oldNames, newNames));
    }

    private static string FormatFile(FileRow r)
    {
        var ver = r.Version is not null ? $", v{r.Version}" : string.Empty;
        return $"{LongName(r.FileName)}, {r.FileSize} bytes{ver}";
    }

    /// <summary>
    /// MSI FileName column uses "8dot3|LongName" format. Returns the long portion when present.
    /// </summary>
    private static string LongName(string msiFileName)
    {
        var pipe = msiFileName.IndexOf('|');
        return pipe >= 0 ? msiFileName[(pipe + 1)..] : msiFileName;
    }

    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffServices(
        IReadOnlyList<ServiceRow> oldRows,
        IReadOnlyList<ServiceRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(r => r.Name, r => FormatService(r), StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(r => r.Name, r => FormatService(r), StringComparer.Ordinal);

        return new PlanDiffSection("Services", DiffMaps(oldMap, newMap));
    }

    private static string FormatService(ServiceRow r) =>
        $"displayName={r.DisplayName ?? r.Name}, type={r.ServiceType}, start={r.StartType}, account={r.StartName ?? "LocalSystem"}";

    // -------------------------------------------------------------------------
    // Registry entries  (keyed by Root\Key\Name)
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffRegistry(
        IReadOnlyList<RegistryRow> oldRows,
        IReadOnlyList<RegistryRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(RegistryKey, StringComparer.Ordinal)
            .ToDictionary(RegistryKey, r => r.Value ?? "(default)", StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(RegistryKey, StringComparer.Ordinal)
            .ToDictionary(RegistryKey, r => r.Value ?? "(default)", StringComparer.Ordinal);

        return new PlanDiffSection("Registry Entries", DiffMaps(oldMap, newMap));
    }

    private static string RegistryKey(RegistryRow r)
    {
        var root = r.Root switch
        {
            0 => "HKCR",
            1 => "HKCU",
            2 => "HKLM",
            3 => "HKU",
            _ => $"ROOT{r.Root}",
        };
        var name = r.Name is not null ? $"\\{r.Name}" : string.Empty;
        return $"{root}\\{r.Key}{name}";
    }

    // -------------------------------------------------------------------------
    // Shortcuts
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffShortcuts(
        IReadOnlyList<ShortcutRow> oldRows,
        IReadOnlyList<ShortcutRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(r => r.Shortcut, r => FormatShortcut(r), StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(r => r.Shortcut, r => FormatShortcut(r), StringComparer.Ordinal);

        var oldNames = oldRows.ToDictionary(r => r.Shortcut, r => LongName(r.Name), StringComparer.Ordinal);
        var newNames = newRows.ToDictionary(r => r.Shortcut, r => LongName(r.Name), StringComparer.Ordinal);

        return new PlanDiffSection("Shortcuts", DiffMaps(oldMap, newMap, oldNames, newNames));
    }

    private static string FormatShortcut(ShortcutRow r) =>
        $"name={LongName(r.Name)}, target={r.Target}, dir={r.Directory_}";

    // -------------------------------------------------------------------------
    // Upgrades
    // -------------------------------------------------------------------------
    private static PlanDiffSection DiffUpgrades(
        IReadOnlyList<UpgradeRow> oldRows,
        IReadOnlyList<UpgradeRow> newRows)
    {
        var oldMap = oldRows
            .OrderBy(r => r.UpgradeCode, StringComparer.Ordinal)
            .ToDictionary(UpgradeKey, r => FormatUpgrade(r), StringComparer.Ordinal);

        var newMap = newRows
            .OrderBy(r => r.UpgradeCode, StringComparer.Ordinal)
            .ToDictionary(UpgradeKey, r => FormatUpgrade(r), StringComparer.Ordinal);

        return new PlanDiffSection("Upgrade Entries", DiffMaps(oldMap, newMap));
    }

    private static string UpgradeKey(UpgradeRow r) =>
        $"{r.UpgradeCode}|{r.VersionMin ?? ""}|{r.VersionMax ?? ""}";

    private static string FormatUpgrade(UpgradeRow r) =>
        $"code={r.UpgradeCode}, min={r.VersionMin ?? "*"}, max={r.VersionMax ?? "*"}, attrs={r.Attributes}";

    // -------------------------------------------------------------------------
    // Generic map diff helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produces an ordered list of <see cref="DiffItem"/> by comparing two string→string maps.
    /// Optional label maps override the key as the human-readable label.
    /// </summary>
    private static IReadOnlyList<DiffItem> DiffMaps(
        Dictionary<string, string> oldMap,
        Dictionary<string, string> newMap,
        Dictionary<string, string>? oldLabels = null,
        Dictionary<string, string>? newLabels = null)
    {
        var allKeys = new SortedSet<string>(
            oldMap.Keys.Concat(newMap.Keys),
            StringComparer.Ordinal);

        var items = new List<DiffItem>(allKeys.Count);

        foreach (var key in allKeys)
        {
            var oldExists = oldMap.TryGetValue(key, out var oldVal);
            var newExists = newMap.TryGetValue(key, out var newVal);

            string label = oldExists
                ? (oldLabels?.GetValueOrDefault(key) ?? key)
                : (newLabels?.GetValueOrDefault(key) ?? key);

            DiffStatus status;
            if (oldExists && newExists)
                status = string.Equals(oldVal, newVal, StringComparison.Ordinal)
                    ? DiffStatus.Unchanged
                    : DiffStatus.Changed;
            else if (newExists)
                status = DiffStatus.Added;
            else
                status = DiffStatus.Removed;

            if (status != DiffStatus.Unchanged)
                items.Add(new DiffItem(status, label, oldExists ? oldVal : null, newExists ? newVal : null));
        }

        return items;
    }
}
