namespace FalkInstaller.Validation;

using FalkInstaller.Models;

public static class ModelValidator
{
    public static ValidationResult Validate(PackageModel package)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(package.Name))
            result.AddError("PKG001", "Package Name is required.");

        if (string.IsNullOrWhiteSpace(package.Manufacturer))
            result.AddError("PKG002", "Package Manufacturer is required.");

        if (package.Version is null)
            result.AddError("PKG003", "Package Version is required.");
        else if (package.Version.Major == 0 && package.Version.Minor == 0 && package.Version.Build == 0)
            result.AddWarning("PKG004", "Package Version is 0.0.0.");

        if (package.Version is not null && package.Version.Major > 255)
            result.AddError("PKG005", "MSI major version cannot exceed 255.");

        if (package.Version is not null && package.Version.Minor > 255)
            result.AddError("PKG006", "MSI minor version cannot exceed 255.");

        if (package.Version is not null && package.Version.Build > 65535)
            result.AddError("PKG007", "MSI build version cannot exceed 65535.");

        if (package.Name.Length > 64)
            result.AddWarning("PKG008", "Package Name exceeds 64 characters, which may cause display issues.");

        if (package.UpgradeCode == Guid.Empty)
            result.AddError("PKG009", "UpgradeCode must not be empty GUID.");

        if (package.ProductCode == Guid.Empty)
            result.AddError("PKG010", "ProductCode must not be empty GUID.");

        if (package.Files.Count == 0 && package.Features.All(f => f.ComponentRefs.Count == 0))
            result.AddWarning("PKG011", "Package has no files. The MSI will be empty.");

        ValidateFeatures(package.Features, result);
        ValidateServices(package.Services, result);
        ValidateShortcuts(package.Shortcuts, result);
        ValidateFonts(package.Fonts, result);
        ValidateIniFiles(package.IniFiles, result);
        ValidatePermissions(package.Permissions, result);
        ValidateFileAssociations(package.FileAssociations, result);
        ValidateCustomActions(package.CustomActions, result);
        ValidateServiceControls(package.ServiceControls, result);
        ValidateServiceDependencies(package.Services, result);
        ValidateRemoveRegistryEntries(package.RemoveRegistryEntries, result);
        ValidateRemoveFiles(package.RemoveFiles, result);
        ValidateCreateFolders(package.CreateFolders, result);
        ValidateMoveFiles(package.MoveFiles, result);
        ValidateDuplicateFiles(package.DuplicateFiles, result);
        ValidateCustomTables(package.CustomTables, result);
        ValidateMediaTemplate(package.MediaTemplate, result);
        ValidateAssemblies(package.Assemblies, result);
        ValidateSigning(package, result);
        ValidateMajorUpgrade(package, result);

        return result;
    }

    private static void ValidateFeatures(IReadOnlyList<FeatureModel> features, ValidationResult result)
    {
        var ids = new HashSet<string>();
        foreach (var feature in features)
        {
            ValidateFeature(feature, ids, result);
        }
    }

    private static void ValidateFeature(FeatureModel feature, HashSet<string> ids, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(feature.Id))
            result.AddError("FEA001", "Feature Id is required.");
        else if (!ids.Add(feature.Id))
            result.AddError("FEA002", $"Duplicate feature Id: '{feature.Id}'.");

        if (string.IsNullOrWhiteSpace(feature.Title))
            result.AddError("FEA003", $"Feature '{feature.Id}' must have a Title.");

        foreach (var condition in feature.Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.Condition))
                result.AddError("FEA004", $"Feature '{feature.Id}' has a condition with an empty condition string.");

            if (condition.Level < 0)
                result.AddWarning("FEA005", $"Feature '{feature.Id}' has a condition with negative level {condition.Level}.");
        }

        foreach (var child in feature.Children)
        {
            ValidateFeature(child, ids, result);
        }
    }

    private static void ValidateServices(IReadOnlyList<ServiceModel> services, ValidationResult result)
    {
        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Name))
                result.AddError("SVC001", "Service Name is required.");

            if (string.IsNullOrWhiteSpace(service.Executable))
                result.AddError("SVC002", $"Service '{service.Name}' must have an Executable.");

            if (service.Account == ServiceAccount.User && string.IsNullOrWhiteSpace(service.UserName))
                result.AddError("SVC003", $"Service '{service.Name}' uses User account but no UserName specified.");

            if (service.Name.Length > 256)
                result.AddError("SVC004", $"Service name '{service.Name}' exceeds 256 characters.");

            if (!string.IsNullOrEmpty(service.Password))
                result.AddWarning("SVC005", $"Service '{service.Name}' has a plaintext password. Consider using a managed service account or store the password securely.");
        }
    }

    private static void ValidateShortcuts(IReadOnlyList<ShortcutModel> shortcuts, ValidationResult result)
    {
        foreach (var shortcut in shortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut.Name))
                result.AddError("SHC001", "Shortcut Name is required.");

            if (string.IsNullOrWhiteSpace(shortcut.TargetFile))
                result.AddError("SHC002", $"Shortcut '{shortcut.Name}' must have a TargetFile.");

            if (shortcut.Locations.Count == 0)
                result.AddWarning("SHC003", $"Shortcut '{shortcut.Name}' has no locations specified.");
        }
    }

    private static void ValidateFonts(IReadOnlyList<FontModel> fonts, ValidationResult result)
    {
        foreach (var font in fonts)
        {
            if (string.IsNullOrWhiteSpace(font.FileName))
                result.AddError("FNT001", "Font FileName is required.");
        }
    }

    private static void ValidateIniFiles(IReadOnlyList<IniFileModel> iniFiles, ValidationResult result)
    {
        foreach (var ini in iniFiles)
        {
            if (string.IsNullOrWhiteSpace(ini.FileName))
                result.AddError("INI001", "INI file FileName is required.");
            if (string.IsNullOrWhiteSpace(ini.Section))
                result.AddError("INI002", $"INI file '{ini.FileName}' must have a Section.");
            if (string.IsNullOrWhiteSpace(ini.Key))
                result.AddError("INI003", $"INI file '{ini.FileName}' must have a Key.");
        }
    }

    private static void ValidatePermissions(IReadOnlyList<PermissionModel> permissions, ValidationResult result)
    {
        foreach (var perm in permissions)
        {
            if (string.IsNullOrWhiteSpace(perm.LockObject))
                result.AddError("PRM001", "Permission LockObject is required.");
            if (string.IsNullOrEmpty(perm.Sddl) && string.IsNullOrEmpty(perm.User))
                result.AddError("PRM002", $"Permission for '{perm.LockObject}' must have either SDDL or User specified.");
        }
    }

    private static void ValidateFileAssociations(IReadOnlyList<FileAssociationModel> associations, ValidationResult result)
    {
        foreach (var assoc in associations)
        {
            if (string.IsNullOrWhiteSpace(assoc.Extension))
                result.AddError("FAS001", "File association Extension is required.");
            if (string.IsNullOrWhiteSpace(assoc.ProgId))
                result.AddError("FAS002", $"File association '{assoc.Extension}' must have a ProgId.");
            if (assoc.Verbs.Count == 0)
                result.AddWarning("FAS003", $"File association '{assoc.Extension}' has no verbs defined.");
        }
    }

    private static void ValidateCustomActions(IReadOnlyList<CustomActionModel> customActions, ValidationResult result)
    {
        foreach (var ca in customActions)
        {
            if (string.IsNullOrWhiteSpace(ca.Id))
                result.AddError("CA001", "Custom action Id is required.");
            if (ca.Type == 0)
                result.AddError("CA002", $"Custom action '{ca.Id}' must have a Type specified.");
            if (string.IsNullOrWhiteSpace(ca.SourceRef))
                result.AddError("CA003", $"Custom action '{ca.Id}' must have a SourceRef.");

            var hasRollback = (ca.Type & CustomActionType.Rollback) != 0;
            var hasCommit = (ca.Type & CustomActionType.Commit) != 0;

            if (hasRollback && hasCommit)
                result.AddError("CA004", $"Custom action '{ca.Id}' cannot be both Rollback and Commit. These are mutually exclusive scheduling options.");

            var hasInScript = (ca.Type & CustomActionType.InScript) != 0;
            var hasNoImpersonate = (ca.Type & CustomActionType.NoImpersonate) != 0;

            if (hasNoImpersonate && !hasInScript)
                result.AddWarning("CA005", $"Custom action '{ca.Id}' has NoImpersonate set but is not a deferred/rollback/commit action. NoImpersonate only applies to in-script actions.");
        }
    }

    private static void ValidateRemoveRegistryEntries(IReadOnlyList<RemoveRegistryModel> entries, ValidationResult result)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                result.AddError("RRG001", "RemoveRegistry Id is required.");

            if (string.IsNullOrWhiteSpace(entry.Key))
                result.AddError("RRG002", $"RemoveRegistry '{entry.Id}' must have a Key.");

            if (entry.Action == RemoveRegistryAction.RemoveValue && string.IsNullOrWhiteSpace(entry.Name))
                result.AddError("RRG003", $"RemoveRegistry '{entry.Id}' uses RemoveValue action but no Name specified.");
        }
    }

    private static void ValidateRemoveFiles(IReadOnlyList<RemoveFileModel> removeFiles, ValidationResult result)
    {
        foreach (var rf in removeFiles)
        {
            if (string.IsNullOrWhiteSpace(rf.DirectoryRef))
                result.AddError("RMF001", $"RemoveFile '{rf.Id}' must have a DirectoryRef.");

            if (!rf.OnInstall && !rf.OnUninstall)
                result.AddError("RMF002", $"RemoveFile '{rf.Id}' must specify at least one of OnInstall or OnUninstall.");
        }
    }

    private static void ValidateCreateFolders(IReadOnlyList<CreateFolderModel> createFolders, ValidationResult result)
    {
        foreach (var cf in createFolders)
        {
            if (string.IsNullOrWhiteSpace(cf.DirectoryRef))
                result.AddError("CRF001", $"CreateFolder '{cf.Id}' must have a DirectoryRef.");
        }
    }

    private static void ValidateMoveFiles(IReadOnlyList<MoveFileModel> moveFiles, ValidationResult result)
    {
        foreach (var mf in moveFiles)
        {
            if (string.IsNullOrWhiteSpace(mf.SourceDirectory))
                result.AddError("MVF001", $"MoveFile '{mf.Id}' must have a SourceDirectory.");

            if (string.IsNullOrWhiteSpace(mf.SourceFileName))
                result.AddError("MVF002", $"MoveFile '{mf.Id}' must have a SourceFileName.");

            if (string.IsNullOrWhiteSpace(mf.DestDirectory))
                result.AddError("MVF003", $"MoveFile '{mf.Id}' must have a DestDirectory.");
        }
    }

    private static void ValidateDuplicateFiles(IReadOnlyList<DuplicateFileModel> duplicateFiles, ValidationResult result)
    {
        foreach (var df in duplicateFiles)
        {
            if (string.IsNullOrWhiteSpace(df.FileRef))
                result.AddError("DPF001", $"DuplicateFile '{df.Id}' must have a FileRef.");
        }
    }

    private static void ValidateServiceControls(IReadOnlyList<ServiceControlModel> controls, ValidationResult result)
    {
        foreach (var control in controls)
        {
            if (string.IsNullOrWhiteSpace(control.ServiceName))
                result.AddError("SCT001", $"ServiceControl '{control.Id}' must have a ServiceName.");

            if (control.Events == ServiceControlEvent.None)
                result.AddError("SCT002", $"ServiceControl '{control.Id}' must have at least one event specified.");
        }
    }

    private static void ValidateServiceDependencies(IReadOnlyList<ServiceModel> services, ValidationResult result)
    {
        foreach (var service in services)
        {
            foreach (var dep in service.TypedDependencies)
            {
                if (string.IsNullOrWhiteSpace(dep.DependsOn))
                    result.AddError("SDP001", $"Service '{service.Name}' has a dependency with no DependsOn value.");
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex CustomTableNameRegex =
        new("^[A-Za-z][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex ColumnNameRegex =
        new("^[A-Za-z_][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void ValidateCustomTables(IReadOnlyList<CustomTableModel> customTables, ValidationResult result)
    {
        foreach (var table in customTables)
        {
            if (string.IsNullOrWhiteSpace(table.Name))
            {
                result.AddError("CTB001", "Custom table Name is required.");
            }
            else
            {
                if (table.Name.Length > 31)
                    result.AddError("CTB002", $"Custom table '{table.Name}' name exceeds 31 characters.");

                if (!CustomTableNameRegex.IsMatch(table.Name))
                    result.AddError("CTB003", $"Custom table '{table.Name}' name must start with a letter and contain only alphanumeric characters and underscores.");
            }

            if (table.Columns.Count == 0)
            {
                result.AddError("CTB004", $"Custom table '{table.Name}' must have at least one column.");
            }
            else
            {
                var hasPrimaryKey = false;
                var columnNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var column in table.Columns)
                {
                    if (string.IsNullOrWhiteSpace(column.Name))
                        result.AddError("CTB005", $"Custom table '{table.Name}' has a column with no name.");
                    else if (!columnNames.Add(column.Name))
                        result.AddError("CTB006", $"Custom table '{table.Name}' has duplicate column name '{column.Name}'.");

                    if (!string.IsNullOrWhiteSpace(column.Name) && !ColumnNameRegex.IsMatch(column.Name))
                        result.AddError("CTB010", $"Custom table '{table.Name}' column '{column.Name}' has an invalid name. Column names must start with a letter or underscore and contain only alphanumeric characters and underscores.");

                    if (column.PrimaryKey)
                        hasPrimaryKey = true;
                }

                if (!hasPrimaryKey)
                    result.AddError("CTB007", $"Custom table '{table.Name}' must have at least one primary key column.");
            }

            // Validate row values match column types
            foreach (var row in table.Rows)
            {
                foreach (var (columnName, value) in row)
                {
                    var column = table.Columns.FirstOrDefault(c => c.Name == columnName);
                    if (column is null)
                    {
                        result.AddError("CTB008", $"Custom table '{table.Name}' row references unknown column '{columnName}'.");
                        continue;
                    }

                    if (value is null)
                        continue;

                    var isValid = column.Type switch
                    {
                        CustomTableColumnType.String => value is string,
                        CustomTableColumnType.Int16 => value is short or int or long,
                        CustomTableColumnType.Int32 => value is int or long,
                        CustomTableColumnType.Binary => value is string, // path to binary
                        CustomTableColumnType.Stream => value is string, // path to stream
                        _ => true
                    };

                    if (!isValid)
                        result.AddError("CTB009", $"Custom table '{table.Name}' column '{columnName}' expects type {column.Type} but got {value.GetType().Name}.");
                }
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex AssemblyVersionRegex =
        new(@"^\d+\.\d+\.\d+\.\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static void ValidateAssemblies(IReadOnlyList<AssemblyModel> assemblies, ValidationResult result)
    {
        foreach (var assembly in assemblies)
        {
            if (string.IsNullOrWhiteSpace(assembly.FileRef))
                result.AddError("ASM001", "Assembly FileRef is required.");

            if (assembly.ApplicationFileRef is null && string.IsNullOrWhiteSpace(assembly.AssemblyPublicKeyToken))
                result.AddWarning("ASM002", $"GAC assembly '{assembly.FileRef}' should have a PublicKeyToken.");

            if (!string.IsNullOrEmpty(assembly.AssemblyVersion) && !AssemblyVersionRegex.IsMatch(assembly.AssemblyVersion))
                result.AddError("ASM003", $"Assembly '{assembly.FileRef}' has invalid version format '{assembly.AssemblyVersion}'. Expected format: x.x.x.x.");
        }
    }

    private static void ValidateMediaTemplate(MediaTemplateModel? mediaTemplate, ValidationResult result)
    {
        if (mediaTemplate is null)
            return;

        if (string.IsNullOrWhiteSpace(mediaTemplate.CabinetTemplate))
            result.AddError("MDT001", "MediaTemplate CabinetTemplate is required.");
        else if (!mediaTemplate.CabinetTemplate.Contains("{0}"))
            result.AddError("MDT002", "MediaTemplate CabinetTemplate must contain '{0}' placeholder for cabinet numbering.");

        if (mediaTemplate.MaximumCabinetSizeInMB < 0)
            result.AddError("MDT003", "MediaTemplate MaximumCabinetSizeInMB cannot be negative.");

        if (mediaTemplate.MaximumUncompressedMediaSize < 0)
            result.AddError("MDT004", "MediaTemplate MaximumUncompressedMediaSize cannot be negative.");
    }

    private static void ValidateSigning(PackageModel package, ValidationResult result)
    {
        if (package.Signing is null)
            return;

        var signing = package.Signing;

        if (string.IsNullOrEmpty(signing.CertificatePath) && string.IsNullOrEmpty(signing.CertificateThumbprint))
            result.AddError("SGN002", "Signing requires either CertificatePath or CertificateThumbprint.");

        if (!string.IsNullOrEmpty(signing.CertificatePath) &&
            signing.CertificatePath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase))
            result.AddWarning("SGN001", "Using a PFX certificate file embeds the private key. Consider using a certificate thumbprint from the certificate store instead.");
    }

    private static void ValidateMajorUpgrade(PackageModel package, ValidationResult result)
    {
        if (package.MajorUpgrade is null)
            return;

        if (package.Upgrade is not null)
            result.AddError("MUP003", "MajorUpgrade and Upgrade table entries cannot both be specified. Use one or the other.");

        if (package.UpgradeCode == Guid.Empty)
            result.AddError("MUP001", "MajorUpgrade requires UpgradeCode to be set on the package.");

        if (!package.MajorUpgrade.AllowDowngrades && string.IsNullOrWhiteSpace(package.MajorUpgrade.DowngradeErrorMessage))
            result.AddError("MUP002", "MajorUpgrade requires DowngradeErrorMessage when AllowDowngrades is false.");
    }
}
