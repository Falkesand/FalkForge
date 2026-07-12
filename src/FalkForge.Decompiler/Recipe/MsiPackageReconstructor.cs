using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Diagnostics;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Pure cross-platform stage that converts raw schema rows (produced by
/// <see cref="TableReadEngine"/>) into a <see cref="PackageModel"/>.
/// Zero <see cref="IMsiTableAccess"/> touches — runs on any OS without msi.dll.
/// </summary>
public static class MsiPackageReconstructor
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

        // Map files
        var fileEntries = new List<FileEntryModel>(fileRows.Count);
        foreach (var f in fileRows)
        {
            var dirId = componentDirMap.GetValueOrDefault(f.Component_, "TARGETDIR");
            var (root, relativePath) = dirResolver.FindRootFolder(dirId);
            var installPath = root is not null
                ? root / relativePath
                : KnownFolder.ProgramFiles / relativePath;

            componentKeyMap.TryGetValue(f.Component_, out var keyPath);
            var isKeyPath = keyPath == f.File;

            componentCondMap.TryGetValue(f.Component_, out var condition);

            // FileName column uses short|long format — extract long name
            var longName = ParseLongFileName(f.FileName);

            fileEntries.Add(new FileEntryModel
            {
                SourcePath = longName,
                TargetDirectory = installPath,
                FileName = longName,
                IsKeyPath = isKeyPath,
                // msidbFileAttributesVital (512) SET means the file is vital; CLEAR means non-vital.
                // FileTableProducer emits the bit only for vital files, so the inverse is a bit test.
                Vital = (f.Attributes & FileAttributesVital) != 0,
                ComponentId = f.Component_,
                ComponentCondition = condition
            });
        }

        // Map features with FeatureComponents wiring
        var featureCompMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fc in featureComponentsRows)
        {
            if (!featureCompMap.TryGetValue(fc.Feature_, out var list))
            {
                list = [];
                featureCompMap[fc.Feature_] = list;
            }
            list.Add(fc.Component_);
        }

        var rawFeatures = featureRows
            .Select(r => new
            {
                r.Feature, ParentId = string.IsNullOrEmpty(r.Feature_Parent) ? null : r.Feature_Parent,
                r.Title, r.Description, r.Display, r.Level, r.Directory_, r.Attributes
            })
            .ToList();

        static List<FeatureModel> BuildTree(
            IReadOnlyList<dynamic> all,
            string? parentId,
            Dictionary<string, List<string>> compMap)
        {
            var result = new List<FeatureModel>();
            foreach (var r in all)
            {
                if (r.ParentId != parentId) continue;
                compMap.TryGetValue((string)r.Feature, out var refs);
                result.Add(new FeatureModel
                {
                    Id = r.Feature,
                    Title = r.Title,
                    Description = r.Description,
                    IsRequired = r.Level == 0,
                    IsDefault = r.Level >= 1,
                    DisplayLevel = r.Level,
                    Children = BuildTree(all, (string)r.Feature, compMap),
                    ComponentRefs = refs ?? []
                });
            }
            return result;
        }

        var features = BuildTree(rawFeatures, null, featureCompMap);

        // Map registry entries
        var registryEntries = registryRows
            .Select(r =>
            {
                var (regValue, regType) = ParseRegistryValue(r.Value);
                return new RegistryEntryModel
                {
                    Root = MapRegistryRoot(r.Root),
                    Key = r.Key,
                    ValueName = r.Name,
                    Value = regValue,
                    ValueType = regType,
                    ComponentId = r.Component_
                };
            })
            .ToList();

        // Map services
        var services = serviceRows
            .Select(r =>
            {
                var startType = r.StartType;
                var startMode = MapStartMode(startType);
                var (account, userName) = MapServiceAccount(r.StartName);
                var deps = r.Dependencies is not null
                    ? r.Dependencies.Split("[~]", StringSplitOptions.RemoveEmptyEntries)
                          .Select(d => d.Trim())
                          .Where(d => !string.IsNullOrEmpty(d))
                          .ToList()
                    : (List<string>)[];
                return new ServiceModel
                {
                    Name = r.Name,
                    DisplayName = r.DisplayName ?? r.Name,
                    Executable = r.ServiceInstall,
                    Description = r.Description,
                    StartMode = startMode,
                    Account = account,
                    UserName = userName,
                    Dependencies = deps
                };
            })
            .ToList();

        // Map shortcuts
        var shortcuts = shortcutRows
            .Select(r =>
            {
                var longName = ParseLongFileName(r.Name);
                var location = MapShortcutLocation(r.Directory_);
                return new ShortcutModel
                {
                    Name = longName,
                    TargetFile = r.Target,
                    Locations = location is not null ? [location.Value] : [],
                    WorkingDirectory = r.WkDir,
                    Arguments = r.Arguments,
                    Description = r.Description,
                    IconFile = r.Icon_,
                    IconIndex = r.IconIndex ?? 0
                };
            })
            .ToList();

        // Map upgrade/downgrade
        var (majorUpgrade, downgrade) = ReconstructUpgrade(upgradeRows);

        // User-defined properties (non-internal)
        var internalProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ProductCode", "ProductName", "ProductVersion", "Manufacturer",
            "UpgradeCode", "ProductLanguage", "ALLUSERS", "ARPNOMODIFY",
            "ARPNOREPAIR", "ARPNOREMOVE", "SecureCustomProperties",
            "MsiLogFileLocation", "INSTALLLEVEL", "REINSTALLMODE",
            "ROOTDRIVE", "LIMITUI", "MsiHiddenProperties"
        };
        var userProps = propertyRows
            .Where(p => !string.IsNullOrEmpty(p.Property) && !internalProps.Contains(p.Property))
            .Select(p => new PropertyModel
            {
                Name = p.Property,
                Value = p.Value,
                IsSecure = p.Property == p.Property.ToUpperInvariant(),
                IsHidden = false
            })
            .ToList();

        // Default install directory from known directory IDs
        InstallPath? defaultInstallDir = null;
        foreach (var dirName in new[] { "INSTALLFOLDER", "INSTALLDIR", "APPDIR" })
        {
            if (directoryRows.Any(d => d.Directory == dirName))
            {
                var (root, relPath) = dirResolver.FindRootFolder(dirName);
                if (root is not null)
                {
                    defaultInstallDir = root / relPath;
                    break;
                }
            }
        }

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

    private const int MsidbUpgradeAttributesMigrateFeatures = 0x00000001;
    private const int MsidbUpgradeAttributesOnlyDetect       = 0x00000002;
    private const int MsidbUpgradeAttributesVersionMaxIncl   = 0x00000200;

    private static (MajorUpgradeModel? Major, DowngradeModel? Downgrade) ReconstructUpgrade(
        IReadOnlyList<UpgradeRow> rows)
    {
        if (rows.Count == 0)
            return (null, null);

        var allowSameVersion = false;
        var migrateFeatures  = false;
        var downgradeBlocked = false;

        foreach (var row in rows)
        {
            var attrs = row.Attributes;
            if ((attrs & MsidbUpgradeAttributesMigrateFeatures) != 0) migrateFeatures  = true;
            if ((attrs & MsidbUpgradeAttributesOnlyDetect)       != 0 &&
                !string.IsNullOrEmpty(row.VersionMax))              downgradeBlocked = true;
            if ((attrs & MsidbUpgradeAttributesVersionMaxIncl)   != 0) allowSameVersion = true;
        }

        var downgrade = downgradeBlocked
            ? new DowngradeModel { AllowDowngrades = false, ErrorMessage = null }
            : new DowngradeModel { AllowDowngrades = true,  ErrorMessage = null };

        return (
            new MajorUpgradeModel
            {
                AllowSameVersionUpgrades = allowSameVersion,
                MigrateFeatures = migrateFeatures
            },
            downgrade);
    }

    private static string ParseLongFileName(string msiFileName)
    {
        if (string.IsNullOrEmpty(msiFileName)) return string.Empty;
        var idx = msiFileName.IndexOf('|');
        return idx >= 0 ? msiFileName[(idx + 1)..] : msiFileName;
    }

    // ── Registry helpers ──────────────────────────────────────────────────────

    private static RegistryRoot MapRegistryRoot(int msiRoot) => msiRoot switch
    {
        0 => RegistryRoot.ClassesRoot,
        1 => RegistryRoot.CurrentUser,
        2 => RegistryRoot.LocalMachine,
        3 => RegistryRoot.Users,
        _ => RegistryRoot.LocalMachine
    };

    /// <summary>
    /// Exact inverse of <c>RegistryTableProducer.EncodeValue</c>. Decodes the Windows Installer
    /// <c>Registry.Value</c> type-prefix convention back into a typed
    /// (<see cref="object"/>, <see cref="RegistryValueType"/>) pair:
    /// <list type="bullet">
    ///   <item><c>[~]</c>-delimited → REG_MULTI_SZ (<see cref="List{String}"/>), covering the
    ///     producer's <c>[~]</c> (empty), <c>[~]value[~]</c> (single) and <c>a[~]b[~]c</c> (multi) forms.</item>
    ///   <item><c>#x</c>+hex → REG_BINARY (<c>byte[]</c>).</item>
    ///   <item><c>#%</c>+text → REG_EXPAND_SZ.</item>
    ///   <item><c>##</c>+text → REG_SZ whose literal value begins with '#' (producer doubles a leading '#').</item>
    ///   <item><c>#</c>+decimal → REG_DWORD (<see cref="int"/>).</item>
    ///   <item>no prefix → REG_SZ.</item>
    /// </list>
    /// The <c>[~]</c> test comes first because REG_MULTI_SZ is typed solely by that delimiter's
    /// presence (there is no separate type column); the <c>#x</c>/<c>#%</c>/<c>##</c> tests precede
    /// the bare <c>#</c> DWORD test so those two-character prefixes are never mis-read as a decimal.
    /// </summary>
    private static (object? Value, RegistryValueType Type) ParseRegistryValue(string? rawValue)
    {
        if (rawValue is null)
            return (null, RegistryValueType.String);

        if (rawValue.Contains("[~]", StringComparison.Ordinal))
            return (DecodeMultiString(rawValue), RegistryValueType.MultiString);

        if (rawValue.StartsWith("#x", StringComparison.Ordinal))
        {
            if (TryParseHex(rawValue.AsSpan(2), out var bytes))
                return (bytes, RegistryValueType.Binary);
            // Malformed hex — keep the raw text rather than silently dropping the value.
            return (rawValue, RegistryValueType.String);
        }

        if (rawValue.StartsWith("#%", StringComparison.Ordinal))
            return (rawValue[2..], RegistryValueType.ExpandString);

        if (rawValue.StartsWith("##", StringComparison.Ordinal))
            return (rawValue[1..], RegistryValueType.String);

        if (rawValue.StartsWith('#'))
        {
            if (int.TryParse(rawValue.AsSpan(1), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var intVal))
                return (intVal, RegistryValueType.DWord);
            // A leading '#' that is neither a type prefix nor a decimal DWORD — treat as literal text.
            return (rawValue, RegistryValueType.String);
        }

        return (rawValue, RegistryValueType.String);
    }

    /// <summary>
    /// Inverts <c>RegistryTableProducer.EncodeMultiString</c>: <c>[~]</c> (empty list),
    /// <c>[~]value[~]</c> (single element, the producer's wrap form), and <c>a[~]b[~]c</c>
    /// (two or more elements, a plain <c>[~]</c>-join). Interior and edge empty elements of the
    /// join form are preserved — splitting without <see cref="StringSplitOptions.RemoveEmptyEntries"/> —
    /// so a decompile→recompile reproduces the same <c>Registry.Value</c> string. The wrap form
    /// <c>[~]value[~]</c> is decoded to the single element <c>[value]</c>; it is inherently ambiguous
    /// with the non-canonical three-element <c>["", value, ""]</c> because the producer's encoding is
    /// not injective, so the producer's own single-element form is chosen.
    /// </summary>
    private static List<string> DecodeMultiString(string rawValue)
    {
        const string separator = "[~]";

        if (rawValue == separator)
            return [];

        // Keep empty segments — an empty segment can be a genuine element of the join form.
        string[] parts = rawValue.Split(separator);

        // The single-element wrap form "[~]value[~]" splits to ["", value, ""]; collapse it back.
        if (parts.Length == 3 && parts[0].Length == 0 && parts[2].Length == 0)
            return [parts[1]];

        return [.. parts];
    }

    /// <summary>
    /// Parses the hex payload of a <c>#x</c> REG_BINARY value. Requires an even length and
    /// only ASCII hex digits (the producer writes <see cref="Convert.ToHexString(byte[])"/>);
    /// returns <see langword="false"/> for anything malformed so the caller can preserve the raw text.
    /// </summary>
    private static bool TryParseHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        if ((hex.Length & 1) != 0)
        {
            bytes = [];
            return false;
        }

        foreach (char c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                bytes = [];
                return false;
            }
        }

        bytes = Convert.FromHexString(hex);
        return true;
    }

    // ── Service helpers ───────────────────────────────────────────────────────

    private static ServiceStartMode MapStartMode(int msiStartType) => msiStartType switch
    {
        0 => ServiceStartMode.Automatic, // SERVICE_BOOT_START mapped to Automatic
        1 => ServiceStartMode.Automatic, // SERVICE_SYSTEM_START mapped to Automatic
        2 => ServiceStartMode.Automatic, // SERVICE_AUTO_START
        3 => ServiceStartMode.Manual,    // SERVICE_DEMAND_START
        4 => ServiceStartMode.Disabled,  // SERVICE_DISABLED
        _ => ServiceStartMode.Automatic
    };

    private static (ServiceAccount Account, string? UserName) MapServiceAccount(string? startName)
    {
        if (string.IsNullOrEmpty(startName) || startName.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.LocalSystem, null);
        if (startName.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase) ||
            startName.Equals("LocalService", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.LocalService, null);
        if (startName.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase) ||
            startName.Equals("NetworkService", StringComparison.OrdinalIgnoreCase))
            return (ServiceAccount.NetworkService, null);

        return (ServiceAccount.User, startName);
    }

    // ── Shortcut helpers ──────────────────────────────────────────────────────

    private static ShortcutLocation? MapShortcutLocation(string directoryId) => directoryId switch
    {
        "DesktopFolder"    => ShortcutLocation.Desktop,
        "StartMenuFolder"  => ShortcutLocation.StartMenu,
        "ProgramMenuFolder" => ShortcutLocation.StartMenu,
        "StartupFolder"    => ShortcutLocation.Startup,
        _ when directoryId.Contains("Desktop",     StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Desktop,
        _ when directoryId.Contains("StartMenu",   StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("ProgramMenu", StringComparison.OrdinalIgnoreCase) => ShortcutLocation.StartMenu,
        _ when directoryId.Contains("Startup",     StringComparison.OrdinalIgnoreCase) => ShortcutLocation.Startup,
        _ => null
    };
}
