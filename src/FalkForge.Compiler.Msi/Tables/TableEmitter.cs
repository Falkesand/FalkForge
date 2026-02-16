using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Tables;

[SupportedOSPlatform("windows")]
internal sealed class TableEmitter
{
    private readonly MsiDatabase _database;

    internal TableEmitter(MsiDatabase database)
    {
        _database = database;
    }

    internal Result<Unit> EmitAllTables(ResolvedPackage resolved)
    {
        var results = new[]
        {
            CreateTables(),
            EmitDirectories(resolved),
            EmitComponents(resolved),
            EmitFiles(resolved),
            EmitFeatures(resolved),
            EmitFeatureComponents(resolved),
            EmitMedia(resolved),
            EmitProperties(resolved),
            EmitRegistry(resolved),
            EmitRemoveRegistry(resolved),
            EmitShortcuts(resolved),
            EmitServices(resolved),
            EmitServiceControls(resolved),
            EmitEnvironment(resolved),
            EmitFonts(resolved),
            EmitUpgrade(resolved),
            EmitMajorUpgrade(resolved),
            EmitLaunchConditions(resolved),
            EmitIniFiles(resolved),
            EmitPermissions(resolved),
            EmitFileAssociations(resolved),
            EmitBinaries(resolved),
            EmitCustomActions(resolved),
            EmitInstallSequences(resolved),
            EmitRemoveFiles(resolved),
            EmitCreateFolders(resolved),
            EmitMoveFiles(resolved),
            EmitDuplicateFiles(resolved),
            EmitConditions(resolved),
            EmitCustomTables(resolved),
            EmitAssemblies(resolved),
            EmitDialogSet(resolved)
        };

        foreach (var result in results)
        {
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitDialogSet(ResolvedPackage resolved)
    {
        var dialogSet = resolved.Package.DialogSet;
        if (dialogSet == MsiDialogSet.None)
            return Unit.Value;

        var dialogEmitter = new DialogEmitter(_database);
        return dialogEmitter.EmitDialogTables(dialogSet, resolved.Package);
    }

    private Result<Unit> CreateTables()
    {
        var tableStatements = new[]
        {
            MsiTableDefinitions.CreateDirectoryTable,
            MsiTableDefinitions.CreateComponentTable,
            MsiTableDefinitions.CreateFileTable,
            MsiTableDefinitions.CreateFeatureTable,
            MsiTableDefinitions.CreateFeatureComponentsTable,
            MsiTableDefinitions.CreateMediaTable,
            MsiTableDefinitions.CreatePropertyTable,
            MsiTableDefinitions.CreateRegistryTable,
            MsiTableDefinitions.CreateShortcutTable,
            MsiTableDefinitions.CreateServiceInstallTable,
            MsiTableDefinitions.CreateServiceControlTable,
            MsiTableDefinitions.CreateUpgradeTable,
            MsiTableDefinitions.CreateLaunchConditionTable,
            MsiTableDefinitions.CreateInstallExecuteSequenceTable,
            MsiTableDefinitions.CreateInstallUISequenceTable,
            MsiTableDefinitions.CreateEnvironmentTable,
            MsiTableDefinitions.CreateFontTable,
            MsiTableDefinitions.CreateIniFileTable,
            MsiTableDefinitions.CreateRemoveIniFileTable,
            MsiTableDefinitions.CreateLockPermissionsTable,
            MsiTableDefinitions.CreateMsiLockPermissionsExTable,
            MsiTableDefinitions.CreateExtensionTable,
            MsiTableDefinitions.CreateVerbTable,
            MsiTableDefinitions.CreateMimeTable,
            MsiTableDefinitions.CreateProgIdTable,
            MsiTableDefinitions.CreateCustomActionTable,
            MsiTableDefinitions.CreateBinaryTable,
            MsiTableDefinitions.CreateRemoveRegistryTable,
            MsiTableDefinitions.CreateRemoveFileTable,
            MsiTableDefinitions.CreateCreateFolderTable,
            MsiTableDefinitions.CreateMoveFileTable,
            MsiTableDefinitions.CreateDuplicateFileTable,
            MsiTableDefinitions.CreateMsiAssemblyTable,
            MsiTableDefinitions.CreateMsiAssemblyNameTable,
            MsiTableDefinitions.CreateConditionTable,
        };

        foreach (var sql in tableStatements)
        {
            var result = _database.Execute(sql);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitDirectories(ResolvedPackage resolved)
    {
        var package = resolved.Package;
        var installDir = package.DefaultInstallDirectory!;

        // TARGETDIR is the root
        var result = InsertDirectoryRow("TARGETDIR", null, "SourceDir");
        if (result.IsFailure) return result;

        // Well-known folder
        result = InsertDirectoryRow(installDir.Root.Token, "TARGETDIR", ".");
        if (result.IsFailure) return result;

        // Build directory tree from install path segments
        var parentId = installDir.Root.Token;
        foreach (var segment in installDir.Segments)
        {
            var dirId = $"D_{SanitizeId(segment)}_{StableHash(parentId)}";
            if (dirId.Length > 72) dirId = dirId[..72];

            result = InsertDirectoryRow(dirId, parentId, segment);
            if (result.IsFailure) return result;
            parentId = dirId;
        }

        // Emit INSTALLDIR property pointing to the leaf directory
        var installDirResult = InsertPropertyRow("INSTALLDIR", parentId);
        if (installDirResult.IsFailure) return installDirResult;

        // Collect all unique directories from components
        var emittedDirs = new HashSet<string> { "TARGETDIR", installDir.Root.Token };
        emittedDirs.Add(parentId);

        foreach (var component in resolved.Components)
        {
            var compDirId = GetDirectoryId(component.Directory);
            if (!emittedDirs.Add(compDirId)) continue;

            // Build path to this directory
            var compParent = component.Directory.Root.Token;
            if (!emittedDirs.Contains(compParent))
            {
                result = InsertDirectoryRow(compParent, "TARGETDIR", ".");
                if (result.IsFailure) return result;
                emittedDirs.Add(compParent);
            }

            var segments = component.Directory.Segments;
            var currentParent = compParent;
            for (var i = 0; i < segments.Count; i++)
            {
                var segDirId = $"D_{SanitizeId(segments[i])}_{StableHash(currentParent)}";
                if (segDirId.Length > 72) segDirId = segDirId[..72];

                if (emittedDirs.Add(segDirId))
                {
                    result = InsertDirectoryRow(segDirId, currentParent, segments[i]);
                    if (result.IsFailure) return result;
                }
                currentParent = segDirId;
            }
        }

        return Unit.Value;
    }

    private Result<Unit> EmitComponents(ResolvedPackage resolved)
    {
        foreach (var component in resolved.Components)
        {
            var dirId = GetDirectoryId(component.Directory);
            var guidStr = component.Guid.ToString("B").ToUpperInvariant();
            var attributes = resolved.Package.Architecture is ProcessorArchitecture.X64 or ProcessorArchitecture.Arm64 ? 256 : 0; // msidbComponentAttributes64bit

            var result = _database.InsertRow(
                "SELECT `Component`, `ComponentId`, `Directory_`, `Attributes`, `Condition`, `KeyPath` FROM `Component`",
                record => record
                    .SetString(1, component.Id)
                    .SetString(2, guidStr)
                    .SetString(3, dirId)
                    .SetInteger(4, attributes)
                    .SetString(5, component.Condition ?? "")
                    .SetString(6, component.KeyPath));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitFiles(ResolvedPackage resolved)
    {
        var sequence = 1;
        foreach (var file in resolved.Files)
        {
            var shortName = GetShortFileName(file.FileName);
            var msiFileName = shortName == file.FileName ? file.FileName : $"{shortName}|{file.FileName}";
            var seq = sequence++;

            var result = _database.InsertRow(
                "SELECT `File`, `Component_`, `FileName`, `FileSize`, `Version`, `Language`, `Attributes`, `Sequence` FROM `File`",
                record => record
                    .SetString(1, file.FileId)
                    .SetString(2, file.ComponentId)
                    .SetString(3, msiFileName)
                    .SetInteger(4, (int)file.FileSize)
                    .SetString(5, "")
                    .SetString(6, "")
                    .SetInteger(7, 512)
                    .SetInteger(8, seq));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitFeatures(ResolvedPackage resolved)
    {
        foreach (var feature in resolved.Package.Features)
        {
            var result = EmitFeature(feature, null);
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitFeature(FeatureModel feature, string? parentId)
    {
        var display = feature.IsDefault ? 1 : 2;
        var level = feature.IsRequired ? 1 : (feature.IsDefault ? 1 : 1000);
        var attributes = feature.IsRequired ? 16 : 0; // UIDisallowAbsent

        var result = _database.InsertRow(
            "SELECT `Feature`, `Feature_Parent`, `Title`, `Description`, `Display`, `Level`, `Directory_`, `Attributes` FROM `Feature`",
            record => record
                .SetString(1, feature.Id)
                .SetString(2, parentId)
                .SetString(3, feature.Title)
                .SetString(4, feature.Description)
                .SetInteger(5, display)
                .SetInteger(6, level)
                .SetString(7, "INSTALLDIR")
                .SetInteger(8, attributes));
        if (result.IsFailure) return result;

        foreach (var child in feature.Children)
        {
            result = EmitFeature(child, feature.Id);
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitFeatureComponents(ResolvedPackage resolved)
    {
        var defaultFeature = resolved.Package.Features.FirstOrDefault()?.Id ?? "Complete";

        foreach (var component in resolved.Components)
        {
            var featureId = component.FeatureRef ?? defaultFeature;
            var result = _database.InsertRow(
                "SELECT `Feature_`, `Component_` FROM `FeatureComponents`",
                record => record
                    .SetString(1, featureId)
                    .SetString(2, component.Id));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitMedia(ResolvedPackage resolved)
    {
        var lastSequence = resolved.Files.Count;
        if (lastSequence == 0) lastSequence = 1;

        var mediaTemplate = resolved.Package.MediaTemplate;
        if (mediaTemplate is not null)
        {
            return EmitMediaFromTemplate(resolved, mediaTemplate, lastSequence);
        }

        return _database.InsertRow(
            "SELECT `DiskId`, `LastSequence`, `DiskPrompt`, `Cabinet`, `VolumeLabel`, `Source` FROM `Media`",
            record => record
                .SetInteger(1, 1)
                .SetInteger(2, lastSequence)
                .SetString(3, "")
                .SetString(4, "#Data.cab")
                .SetString(5, "")
                .SetString(6, ""));
    }

    private Result<Unit> EmitMediaFromTemplate(ResolvedPackage resolved, MediaTemplateModel template, int lastSequence)
    {
        var maxCabSizeBytes = template.MaximumCabinetSizeInMB > 0
            ? (long)template.MaximumCabinetSizeInMB * 1024 * 1024
            : 0;

        // Calculate total file size to determine cabinet splitting
        var totalSize = resolved.Files.Sum(f => f.FileSize);

        // Determine how many cabinets we need
        int cabinetCount;
        if (maxCabSizeBytes > 0 && totalSize > maxCabSizeBytes)
        {
            cabinetCount = (int)Math.Ceiling((double)totalSize / maxCabSizeBytes);
        }
        else
        {
            cabinetCount = 1;
        }

        var filesPerCabinet = lastSequence > 0
            ? (int)Math.Ceiling((double)lastSequence / cabinetCount)
            : 1;

        for (var i = 0; i < cabinetCount; i++)
        {
            var diskId = i + 1;
            var cabLastSeq = Math.Min((i + 1) * filesPerCabinet, lastSequence);
            var cabinetName = string.Format(template.CabinetTemplate, diskId);

            // Prefix with # for embedded cabinets
            if (template.EmbedCabinet)
            {
                cabinetName = $"#{cabinetName}";
            }

            var result = _database.InsertRow(
                "SELECT `DiskId`, `LastSequence`, `DiskPrompt`, `Cabinet`, `VolumeLabel`, `Source` FROM `Media`",
                record => record
                    .SetInteger(1, diskId)
                    .SetInteger(2, cabLastSeq)
                    .SetString(3, "")
                    .SetString(4, cabinetName)
                    .SetString(5, "")
                    .SetString(6, ""));
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitProperties(ResolvedPackage resolved)
    {
        var package = resolved.Package;
        var props = new Dictionary<string, string>
        {
            ["ProductName"] = package.Name,
            ["Manufacturer"] = package.Manufacturer,
            ["ProductVersion"] = package.Version.ToString(3),
            ["ProductCode"] = package.ProductCode.ToString("B").ToUpperInvariant(),
            ["UpgradeCode"] = package.UpgradeCode.ToString("B").ToUpperInvariant(),
            ["ProductLanguage"] = "1033",
            ["ALLUSERS"] = package.Scope == InstallScope.PerMachine ? "1" : "",
        };

        if (package.EnableRestartManager)
        {
            props["MSIRMSHUTDOWN"] = "2";
        }

        foreach (var prop in package.Properties)
        {
            props[prop.Name] = prop.Value;
        }

        foreach (var (name, value) in props)
        {
            if (string.IsNullOrEmpty(value)) continue;
            var result = InsertPropertyRow(name, value);
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitRegistry(ResolvedPackage resolved)
    {
        var index = 0;
        foreach (var entry in resolved.Package.RegistryEntries)
        {
            var root = entry.Root switch
            {
                RegistryRoot.ClassesRoot => 0,
                RegistryRoot.CurrentUser => 1,
                RegistryRoot.LocalMachine => 2,
                RegistryRoot.Users => 3,
                _ => 2
            };

            var regId = $"Reg_{index++:D4}";
            var componentId = entry.ComponentId ?? resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
            var valueStr = entry.Value?.ToString() ?? "";

            var result = _database.InsertRow(
                "SELECT `Registry`, `Root`, `Key`, `Name`, `Value`, `Component_` FROM `Registry`",
                record => record
                    .SetString(1, regId)
                    .SetInteger(2, root)
                    .SetString(3, entry.Key)
                    .SetString(4, entry.ValueName)
                    .SetString(5, valueStr)
                    .SetString(6, componentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitRemoveRegistry(ResolvedPackage resolved)
    {
        var entries = resolved.Package.RemoveRegistryEntries;
        if (entries.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var entry in entries)
        {
            var root = entry.Root switch
            {
                RegistryRoot.ClassesRoot => 0,
                RegistryRoot.CurrentUser => 1,
                RegistryRoot.LocalMachine => 2,
                RegistryRoot.Users => 3,
                _ => 2
            };

            var entryComponentId = entry.ComponentRef ?? componentId;
            var name = entry.Action == RemoveRegistryAction.RemoveKey ? null : entry.Name;

            var result = _database.InsertRow(
                "SELECT `RemoveRegistry`, `Root`, `Key`, `Name`, `Component_` FROM `RemoveRegistry`",
                record => record
                    .SetString(1, entry.Id)
                    .SetInteger(2, root)
                    .SetString(3, entry.Key)
                    .SetString(4, name)
                    .SetString(5, entryComponentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitShortcuts(ResolvedPackage resolved)
    {
        var index = 0;
        foreach (var shortcut in resolved.Package.Shortcuts)
        {
            foreach (var location in shortcut.Locations)
            {
                var dirId = location switch
                {
                    ShortcutLocation.Desktop => "DesktopFolder",
                    ShortcutLocation.StartMenu => shortcut.StartMenuSubfolder is not null
                        ? $"SM_{SanitizeId(shortcut.StartMenuSubfolder)}"
                        : "ProgramMenuFolder",
                    ShortcutLocation.Startup => "StartupFolder",
                    _ => "DesktopFolder"
                };

                var shortcutId = $"SC_{index++:D4}";
                var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
                var target = $"[INSTALLDIR]{shortcut.TargetFile}";

                var result = _database.InsertRow(
                    "SELECT `Shortcut`, `Directory_`, `Name`, `Component_`, `Target`, `Arguments`, `Description`, `Hotkey`, `Icon_`, `IconIndex`, `ShowCmd`, `WkDir` FROM `Shortcut`",
                    record => record
                        .SetString(1, shortcutId)
                        .SetString(2, dirId)
                        .SetString(3, shortcut.Name)
                        .SetString(4, componentId)
                        .SetString(5, target)
                        .SetString(6, shortcut.Arguments)
                        .SetString(7, shortcut.Description)
                        // Fields 8 (Hotkey), 9 (Icon_), 10 (IconIndex), 11 (ShowCmd) left unset (null)
                        .SetString(12, "INSTALLDIR"));
                if (result.IsFailure) return result;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> EmitServices(ResolvedPackage resolved)
    {
        foreach (var service in resolved.Package.Services)
        {
            var startType = service.StartMode switch
            {
                ServiceStartMode.Automatic => 2,
                ServiceStartMode.Manual => 3,
                ServiceStartMode.Disabled => 4,
                ServiceStartMode.DelayedAutomatic => 2,
                _ => 2
            };

            var startName = service.Account switch
            {
                ServiceAccount.LocalSystem => "LocalSystem",
                ServiceAccount.LocalService => @"NT AUTHORITY\LocalService",
                ServiceAccount.NetworkService => @"NT AUTHORITY\NetworkService",
                ServiceAccount.User => service.UserName,
                _ => "LocalSystem"
            };

            var componentId = resolved.Components.FirstOrDefault(c =>
                c.Files.Any(f => f.FileName.Equals(service.Executable, StringComparison.OrdinalIgnoreCase)))?.Id
                ?? resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

            var svcId = $"SVC_{SanitizeId(service.Name)}";
            if (svcId.Length > 72) svcId = svcId[..72];

            // Build dependencies string: typed dependencies take precedence over legacy string list
            string? dependencies = null;
            if (service.TypedDependencies.Count > 0)
            {
                var depParts = service.TypedDependencies.Select(d => d.Group ? $"+{d.DependsOn}" : d.DependsOn);
                dependencies = string.Join("[~]", depParts);
            }
            else if (service.Dependencies.Count > 0)
            {
                dependencies = string.Join("[~]", service.Dependencies);
            }

            var result = _database.InsertRow(
                "SELECT `ServiceInstall`, `Name`, `DisplayName`, `ServiceType`, `StartType`, `ErrorControl`, " +
                "`LoadOrderGroup`, `Dependencies`, `StartName`, `Password`, `Arguments`, `Component_`, `Description` FROM `ServiceInstall`",
                record => record
                    .SetString(1, svcId)
                    .SetString(2, service.Name)
                    .SetString(3, service.DisplayName)
                    .SetInteger(4, 16)
                    .SetInteger(5, startType)
                    .SetInteger(6, 1)
                    .SetString(7, null)
                    .SetString(8, dependencies)
                    .SetString(9, startName)
                    .SetString(10, service.Password)
                    .SetString(11, null)
                    .SetString(12, componentId)
                    .SetString(13, service.Description));
            if (result.IsFailure) return result;

            // ServiceControl: start on install, stop on uninstall
            result = _database.InsertRow(
                "SELECT `ServiceControl`, `Name`, `Event`, `Arguments`, `Wait`, `Component_` FROM `ServiceControl`",
                record => record
                    .SetString(1, $"{svcId}_Start")
                    .SetString(2, service.Name)
                    .SetInteger(3, 1)
                    .SetString(4, null)
                    .SetInteger(5, 1)
                    .SetString(6, componentId));
            if (result.IsFailure) return result;

            result = _database.InsertRow(
                "SELECT `ServiceControl`, `Name`, `Event`, `Arguments`, `Wait`, `Component_` FROM `ServiceControl`",
                record => record
                    .SetString(1, $"{svcId}_Stop")
                    .SetString(2, service.Name)
                    .SetInteger(3, 2)
                    .SetString(4, null)
                    .SetInteger(5, 1)
                    .SetString(6, componentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitServiceControls(ResolvedPackage resolved)
    {
        var controls = resolved.Package.ServiceControls;
        if (controls.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var control in controls)
        {
            var ctrlComponentId = control.ComponentRef ?? componentId;
            var eventValue = (int)control.Events;
            var waitValue = control.Wait ? 1 : 0;

            var result = _database.InsertRow(
                "SELECT `ServiceControl`, `Name`, `Event`, `Arguments`, `Wait`, `Component_` FROM `ServiceControl`",
                record => record
                    .SetString(1, control.Id)
                    .SetString(2, control.ServiceName)
                    .SetInteger(3, eventValue)
                    .SetString(4, control.Arguments)
                    .SetInteger(5, waitValue)
                    .SetString(6, ctrlComponentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitEnvironment(ResolvedPackage resolved)
    {
        var envVars = resolved.Package.EnvironmentVariables;
        if (envVars.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        for (var index = 0; index < envVars.Count; index++)
        {
            var envVar = envVars[index];
            var envId = $"ENV_{index:D4}";
            var encodedName = EnvironmentEncoding.EncodeName(envVar.Name, envVar.Action);
            var encodedValue = EnvironmentEncoding.EncodeValue(envVar.Value, envVar.Action, envVar.Separator);

            var result = _database.InsertRow(
                "SELECT `Environment`, `Name`, `Value`, `Component_` FROM `Environment`",
                record => record
                    .SetString(1, envId)
                    .SetString(2, encodedName)
                    .SetString(3, encodedValue)
                    .SetString(4, componentId));
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitFonts(ResolvedPackage resolved)
    {
        var fonts = resolved.Package.Fonts;
        if (fonts.Count == 0) return Unit.Value;

        foreach (var font in fonts)
        {
            // Find the file ID for this font file
            var fileId = resolved.Files.FirstOrDefault(f =>
                f.FileName.Equals(font.FileName, StringComparison.OrdinalIgnoreCase))?.FileId;
            if (fileId is null) continue;

            var result = _database.InsertRow(
                "SELECT `File_`, `FontTitle` FROM `Font`",
                record => record
                    .SetString(1, fileId)
                    .SetString(2, font.FontTitle));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitIniFiles(ResolvedPackage resolved)
    {
        var iniFiles = resolved.Package.IniFiles;
        if (iniFiles.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        for (var index = 0; index < iniFiles.Count; index++)
        {
            var ini = iniFiles[index];
            var iniId = $"INI_{index:D4}";
            var action = (int)ini.Action;

            var result = _database.InsertRow(
                "SELECT `IniFile`, `FileName`, `DirProperty`, `Section`, `Key`, `Value`, `Action`, `Component_` FROM `IniFile`",
                record => record
                    .SetString(1, iniId)
                    .SetString(2, ini.FileName)
                    .SetString(3, ini.DirProperty)
                    .SetString(4, ini.Section)
                    .SetString(5, ini.Key)
                    .SetString(6, ini.Value)
                    .SetInteger(7, action)
                    .SetString(8, componentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitPermissions(ResolvedPackage resolved)
    {
        var perms = resolved.Package.Permissions;
        if (perms.Count == 0) return Unit.Value;

        for (var index = 0; index < perms.Count; index++)
        {
            var perm = perms[index];

            if (!string.IsNullOrEmpty(perm.Sddl))
            {
                var permId = $"PRM_{index:D4}";
                var result = _database.InsertRow(
                    "SELECT `MsiLockPermissionsEx`, `LockObject`, `Table`, `SDDLText`, `Condition` FROM `MsiLockPermissionsEx`",
                    record => record
                        .SetString(1, permId)
                        .SetString(2, perm.LockObject)
                        .SetString(3, perm.Table)
                        .SetString(4, perm.Sddl)
                        .SetString(5, null));
                if (result.IsFailure) return result;
            }
            else if (!string.IsNullOrEmpty(perm.User))
            {
                var result = _database.InsertRow(
                    "SELECT `LockObject`, `Table`, `Domain`, `User`, `Permission` FROM `LockPermissions`",
                    record => record
                        .SetString(1, perm.LockObject)
                        .SetString(2, perm.Table)
                        .SetString(3, perm.Domain)
                        .SetString(4, perm.User)
                        .SetInteger(5, perm.Permission));
                if (result.IsFailure) return result;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> EmitUpgrade(ResolvedPackage resolved)
    {
        var package = resolved.Package;
        if (package.Upgrade is null) return Unit.Value;

        var upgradeCode = package.UpgradeCode.ToString("B").ToUpperInvariant();
        var versionStr = package.Version.ToString(3);

        // Detect and remove older versions
        var result = _database.InsertRow(
            "SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`",
            record => record
                .SetString(1, upgradeCode)
                .SetString(2, "0.0.0")
                .SetString(3, versionStr)
                .SetString(4, "")
                .SetInteger(5, 256)
                .SetString(6, "")
                .SetString(7, "OLDERVERSIONFOUND"));
        if (result.IsFailure) return result;

        // Detect newer versions (prevent downgrade)
        if (!package.Upgrade.AllowDowngrades)
        {
            result = _database.InsertRow(
                "SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`",
                record => record
                    .SetString(1, upgradeCode)
                    .SetString(2, versionStr)
                    .SetString(3, "")
                    .SetString(4, "")
                    .SetInteger(5, 258)
                    .SetString(6, "")
                    .SetString(7, "NEWERVERSIONFOUND"));
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitMajorUpgrade(ResolvedPackage resolved)
    {
        var package = resolved.Package;
        if (package.MajorUpgrade is null) return Unit.Value;

        // Defense-in-depth: skip MajorUpgrade emission if Upgrade table entries are also configured.
        // The validator should catch this conflict, but guard here to prevent duplicate table rows.
        if (package.Upgrade is not null) return Unit.Value;

        var majorUpgrade = package.MajorUpgrade;
        var upgradeCode = package.UpgradeCode.ToString("B").ToUpperInvariant();
        var versionStr = package.Version.ToString(3);

        // Row to detect older versions (remove them)
        var attributes = majorUpgrade.AllowSameVersionUpgrades ? 0 : 256; // 256 = VersionMaxInclusive
        var result = _database.InsertRow(
            "SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`",
            record => record
                .SetString(1, upgradeCode)
                .SetString(2, "0.0.0")
                .SetString(3, versionStr)
                .SetString(4, "")
                .SetInteger(5, attributes)
                .SetString(6, "")
                .SetString(7, "OLDERVERSIONFOUND"));
        if (result.IsFailure) return result;

        // If downgrades not allowed, add row to detect newer versions and a launch condition to block
        if (!majorUpgrade.AllowDowngrades)
        {
            result = _database.InsertRow(
                "SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`, `Remove`, `ActionProperty` FROM `Upgrade`",
                record => record
                    .SetString(1, upgradeCode)
                    .SetString(2, versionStr)
                    .SetString(3, "")
                    .SetString(4, "")
                    .SetInteger(5, 2) // OnlyDetect
                    .SetString(6, "")
                    .SetString(7, "NEWERVERSIONFOUND"));
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitLaunchConditions(ResolvedPackage resolved)
    {
        var package = resolved.Package;

        // Add downgrade prevention condition
        if (package.Upgrade is not null && !package.Upgrade.AllowDowngrades)
        {
            var msg = package.Upgrade.DowngradeErrorMessage ?? "A newer version is already installed.";
            var result = InsertLaunchConditionRow("NOT NEWERVERSIONFOUND", msg);
            if (result.IsFailure) return result;
        }

        // Add MajorUpgrade downgrade prevention condition
        if (package.MajorUpgrade is not null && !package.MajorUpgrade.AllowDowngrades)
        {
            var msg = package.MajorUpgrade.DowngradeErrorMessage ?? "A newer version is already installed.";
            var result = InsertLaunchConditionRow("NOT NEWERVERSIONFOUND", msg);
            if (result.IsFailure) return result;
        }

        foreach (var condition in package.LaunchConditions)
        {
            var result = InsertLaunchConditionRow(condition.Condition, condition.Message);
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitInstallSequences(ResolvedPackage resolved)
    {
        var package = resolved.Package;

        // Build mutable list of standard execute sequence actions
        var actions = new List<(string Action, int Sequence, string Condition)>
        {
            ("AppSearch", 50, ""),
            ("LaunchConditions", 100, ""),
            ("ValidateProductID", 700, ""),
            ("CostInitialize", 800, ""),
            ("FileCost", 900, ""),
            ("CostFinalize", 1000, ""),
            ("InstallValidate", 1400, ""),
            ("InstallInitialize", 1500, ""),
            ("ProcessComponents", 1600, ""),
            ("UnpublishFeatures", 1800, ""),
            ("RemoveRegistryValues", 2600, ""),
            ("RemoveShortcuts", 3200, ""),
            ("RemoveFiles", 3500, ""),
            ("InstallFiles", 4000, ""),
            ("CreateShortcuts", 4500, ""),
            ("WriteRegistryValues", 5000, ""),
            ("RegisterUser", 6000, ""),
            ("RegisterProduct", 6100, ""),
            ("PublishFeatures", 6300, ""),
            ("PublishProduct", 6400, ""),
            ("InstallFinalize", 6600, ""),
        };

        // Conditional: Font actions
        if (package.Fonts.Count > 0)
        {
            actions.Add(("UnregisterFonts", 3100, ""));
            actions.Add(("RegisterFonts", 5300, ""));
        }

        // Conditional: INI file actions
        if (package.IniFiles.Count > 0)
        {
            actions.Add(("RemoveIniValues", 3400, ""));
            actions.Add(("WriteIniValues", 5100, ""));
        }

        // Conditional: File association actions
        if (package.FileAssociations.Count > 0)
        {
            actions.Add(("UnregisterExtensionInfo", 3000, ""));
            actions.Add(("RegisterExtensionInfo", 5500, ""));
        }

        // Conditional: Upgrade actions
        if (package.Upgrade is not null)
        {
            actions.Add(("RemoveExistingProducts", 1450, ""));
        }

        // Conditional: MajorUpgrade actions
        if (package.MajorUpgrade is not null)
        {
            var removeSeq = GetRemoveExistingProductsSequence(package.MajorUpgrade.Schedule);
            actions.Add(("RemoveExistingProducts", removeSeq, ""));

            if (package.MajorUpgrade.MigrateFeatures)
            {
                actions.Add(("MigrateFeatureStates", 1401, ""));
            }
        }

        // Conditional: Environment variable actions
        if (package.EnvironmentVariables.Count > 0)
        {
            actions.Add(("RemoveEnvironmentStrings", 3300, ""));
            actions.Add(("WriteEnvironmentStrings", 5200, ""));
        }

        // Conditional: Service actions (emit when either Services or ServiceControls are defined)
        if (package.Services.Count > 0 || package.ServiceControls.Count > 0)
        {
            actions.Add(("StopServices", 1900, ""));
            actions.Add(("DeleteServices", 2000, ""));
            actions.Add(("InstallServices", 5800, ""));
            actions.Add(("StartServices", 5900, ""));
        }

        // Conditional: CreateFolders action
        if (package.CreateFolders.Count > 0)
        {
            actions.Add(("CreateFolders", 3700, ""));
            actions.Add(("RemoveFolders", 3600, ""));
        }

        // Conditional: MoveFiles action
        if (package.MoveFiles.Count > 0)
        {
            actions.Add(("MoveFiles", 3800, ""));
        }

        // Conditional: DuplicateFiles action
        if (package.DuplicateFiles.Count > 0)
        {
            actions.Add(("DuplicateFiles", 4210, ""));
            actions.Add(("RemoveDuplicateFiles", 3180, ""));
        }

        // Merge custom execute sequence actions
        MergeCustomSequenceActions(actions, package.ExecuteSequenceActions);

        // Sort by sequence number and emit
        actions.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        foreach (var (action, sequence, condition) in actions)
        {
            var result = InsertSequenceRow("InstallExecuteSequence", action, sequence, condition);
            if (result.IsFailure) return result;
        }

        // Emit InstallUISequence if configured
        var uiResult = EmitUISequence(package);
        if (uiResult.IsFailure) return uiResult;

        return Unit.Value;
    }

    private Result<Unit> EmitUISequence(PackageModel package)
    {
        if (package.UISequenceActions.Count == 0) return Unit.Value;

        // If a DialogSet is active, the DialogEmitter already writes the InstallUISequence
        // baseline (AppSearch, LaunchConditions, CostInitialize, etc.). In that case, only
        // emit the custom actions on top of the dialog emitter's baseline -- do not duplicate
        // the standard actions.
        if (package.DialogSet != MsiDialogSet.None)
        {
            // Build the baseline action list so MergeCustomSequenceActions can resolve
            // relative positions (After/Before), but only emit the custom actions themselves.
            var baseline = new List<(string Action, int Sequence, string Condition)>
            {
                ("AppSearch", 50, ""),
                ("LaunchConditions", 100, ""),
                ("ValidateProductID", 700, ""),
                ("CostInitialize", 800, ""),
                ("FileCost", 900, ""),
                ("CostFinalize", 1000, ""),
                ("ExecuteAction", 1300, ""),
            };

            var baselineNames = new HashSet<string>(baseline.Select(a => a.Action));
            MergeCustomSequenceActions(baseline, package.UISequenceActions);

            baseline.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

            foreach (var (action, sequence, condition) in baseline)
            {
                // Skip baseline actions -- they were already emitted by the DialogEmitter
                if (baselineNames.Contains(action)) continue;

                var result = InsertSequenceRow("InstallUISequence", action, sequence, condition);
                if (result.IsFailure) return result;
            }

            return Unit.Value;
        }

        // No dialog set -- emit the full standard UI sequence baseline plus custom actions
        var actions = new List<(string Action, int Sequence, string Condition)>
        {
            ("AppSearch", 50, ""),
            ("LaunchConditions", 100, ""),
            ("ValidateProductID", 700, ""),
            ("CostInitialize", 800, ""),
            ("FileCost", 900, ""),
            ("CostFinalize", 1000, ""),
            ("ExecuteAction", 1300, ""),
        };

        MergeCustomSequenceActions(actions, package.UISequenceActions);

        actions.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));

        foreach (var (action, sequence, condition) in actions)
        {
            var result = InsertSequenceRow("InstallUISequence", action, sequence, condition);
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private static void MergeCustomSequenceActions(
        List<(string Action, int Sequence, string Condition)> actions,
        IReadOnlyList<SequenceActionModel> customActions)
    {
        foreach (var customAction in customActions)
        {
            var sequence = ResolveSequenceNumber(customAction.Position, actions);
            sequence = EnsureUniqueSequence(sequence, actions);
            actions.Add((customAction.ActionName, sequence, customAction.Condition ?? ""));
        }
    }

    private static int ResolveSequenceNumber(
        ActionPosition position,
        List<(string Action, int Sequence, string Condition)> existingActions)
    {
        return position switch
        {
            ActionPosition.AtNumber at => at.SequenceNumber,
            ActionPosition.AfterAction after => FindReferenceSequence(after.ReferenceAction, existingActions) + 1,
            ActionPosition.BeforeAction before => FindReferenceSequence(before.ReferenceAction, existingActions) - 1,
            _ => 4001
        };
    }

    private static int FindReferenceSequence(
        string referenceAction,
        List<(string Action, int Sequence, string Condition)> actions)
    {
        foreach (var (action, sequence, _) in actions)
        {
            if (string.Equals(action, referenceAction, StringComparison.Ordinal))
                return sequence;
        }

        // Fallback: check well-known action names
        return referenceAction switch
        {
            "InstallInitialize" => 1500,
            "InstallFiles" => 4000,
            "InstallFinalize" => 6600,
            "WriteRegistryValues" => 5000,
            "CreateShortcuts" => 4500,
            "RemoveFiles" => 3500,
            _ => 4000
        };
    }

    private static int EnsureUniqueSequence(
        int desiredSequence,
        List<(string Action, int Sequence, string Condition)> actions)
    {
        const int maxIterations = 100;

        var occupied = new HashSet<int>();
        foreach (var (_, seq, _) in actions)
            occupied.Add(seq);

        var candidate = desiredSequence;
        var iterations = 0;
        while (occupied.Contains(candidate))
        {
            candidate++;
            iterations++;
            if (iterations >= maxIterations)
            {
                // Safety limit reached -- sequences are too densely packed.
                // Return current candidate and accept the risk of a collision warning
                // rather than looping indefinitely.
                break;
            }
        }

        return candidate;
    }

    private Result<Unit> EmitConditions(ResolvedPackage resolved)
    {
        foreach (var feature in resolved.Package.Features)
        {
            var result = EmitFeatureConditions(feature);
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitFeatureConditions(FeatureModel feature)
    {
        foreach (var condition in feature.Conditions)
        {
            var result = _database.InsertRow(
                "SELECT `Feature_`, `Level`, `Condition` FROM `Condition`",
                record => record
                    .SetString(1, feature.Id)
                    .SetInteger(2, condition.Level)
                    .SetString(3, condition.Condition));
            if (result.IsFailure) return result;
        }

        foreach (var child in feature.Children)
        {
            var result = EmitFeatureConditions(child);
            if (result.IsFailure) return result;
        }

        return Unit.Value;
    }

    private Result<Unit> InsertExecuteSequenceRow(string action, int sequence)
    {
        return InsertSequenceRow("InstallExecuteSequence", action, sequence, "");
    }

    private static readonly HashSet<string> AllowedSequenceTables = new(StringComparer.Ordinal)
    {
        "InstallExecuteSequence",
        "InstallUISequence"
    };

    private Result<Unit> InsertSequenceRow(string tableName, string action, int sequence, string condition)
    {
        if (!AllowedSequenceTables.Contains(tableName))
        {
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"Table name '{tableName}' is not an allowed sequence table. " +
                $"Only {string.Join(", ", AllowedSequenceTables)} are permitted.");
        }

        return _database.InsertRow(
            $"SELECT `Action`, `Condition`, `Sequence` FROM `{tableName}`",
            record => record
                .SetString(1, action)
                .SetString(2, condition)
                .SetInteger(3, sequence));
    }

    private Result<Unit> EmitFileAssociations(ResolvedPackage resolved)
    {
        var assocs = resolved.Package.FileAssociations;
        if (assocs.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
        var featureId = resolved.Package.Features.FirstOrDefault()?.Id ?? "Complete";

        foreach (var assoc in assocs)
        {
            var ext = assoc.Extension.TrimStart('.');

            // ProgId table
            var progResult = _database.InsertRow(
                "SELECT `ProgId`, `ProgId_Parent`, `Class_`, `Description`, `Icon_`, `IconIndex` FROM `ProgId`",
                record => record
                    .SetString(1, assoc.ProgId)
                    .SetString(2, null)
                    .SetString(3, null)
                    .SetString(4, assoc.Description)
                    .SetString(5, null)
                    .SetInteger(6, assoc.IconIndex));
            if (progResult.IsFailure) return progResult;

            // Extension table
            var extResult = _database.InsertRow(
                "SELECT `Extension`, `Component_`, `ProgId_`, `MIME_`, `Feature_` FROM `Extension`",
                record => record
                    .SetString(1, ext)
                    .SetString(2, componentId)
                    .SetString(3, assoc.ProgId)
                    .SetString(4, assoc.ContentType)
                    .SetString(5, featureId));
            if (extResult.IsFailure) return extResult;

            // MIME table (if ContentType specified)
            if (!string.IsNullOrEmpty(assoc.ContentType))
            {
                var mimeResult = _database.InsertRow(
                    "SELECT `ContentType`, `Extension_`, `CLSID` FROM `MIME`",
                    record => record
                        .SetString(1, assoc.ContentType)
                        .SetString(2, ext)
                        .SetString(3, null));
                if (mimeResult.IsFailure) return mimeResult;
            }

            // Verb table
            foreach (var verb in assoc.Verbs)
            {
                var verbResult = _database.InsertRow(
                    "SELECT `Extension_`, `Verb`, `Sequence`, `Command`, `Argument` FROM `Verb`",
                    record => record
                        .SetString(1, ext)
                        .SetString(2, verb.Verb)
                        .SetInteger(3, verb.Sequence)
                        .SetString(4, verb.Command)
                        .SetString(5, verb.Argument));
                if (verbResult.IsFailure) return verbResult;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> EmitBinaries(ResolvedPackage resolved)
    {
        var binaries = resolved.Package.Binaries;
        if (binaries.Count == 0) return Unit.Value;

        foreach (var binary in binaries)
        {
            var result = _database.InsertRow(
                "SELECT `Name`, `Data` FROM `Binary`",
                record => record
                    .SetString(1, binary.Name)
                    .SetStream(2, binary.SourcePath));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitCustomActions(ResolvedPackage resolved)
    {
        var cas = resolved.Package.CustomActions;
        if (cas.Count == 0) return Unit.Value;

        foreach (var ca in cas)
        {
            var result = _database.InsertRow(
                "SELECT `Action`, `Type`, `Source`, `Target`, `ExtendedType` FROM `CustomAction`",
                record => record
                    .SetString(1, ca.Id)
                    .SetInteger(2, ca.Type)
                    .SetString(3, ca.SourceRef)
                    .SetString(4, ca.Target)
                    .SetInteger(5, 0));
            if (result.IsFailure) return result;

            // Add to InstallExecuteSequence if sequence info provided
            if (ca.After is not null || ca.Before is not null || ca.Sequence.HasValue)
            {
                var seq = ca.Sequence ?? GetSequenceForAction(ca.After, ca.Before);
                var seqResult = _database.InsertRow(
                    "SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`",
                    record => record
                        .SetString(1, ca.Id)
                        .SetString(2, ca.Condition ?? "")
                        .SetInteger(3, seq));
                if (seqResult.IsFailure) return seqResult;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> EmitRemoveFiles(ResolvedPackage resolved)
    {
        var removeFiles = resolved.Package.RemoveFiles;
        if (removeFiles.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var rf in removeFiles)
        {
            var installMode = (rf.OnInstall ? 1 : 0) | (rf.OnUninstall ? 2 : 0);
            var entryComponentId = rf.ComponentRef ?? componentId;

            var result = _database.InsertRow(
                "SELECT `FileKey`, `Component_`, `FileName`, `DirProperty`, `InstallMode` FROM `RemoveFile`",
                record => record
                    .SetString(1, rf.Id)
                    .SetString(2, entryComponentId)
                    .SetString(3, rf.FileName)
                    .SetString(4, rf.DirectoryRef)
                    .SetInteger(5, installMode));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitCreateFolders(ResolvedPackage resolved)
    {
        var createFolders = resolved.Package.CreateFolders;
        if (createFolders.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var cf in createFolders)
        {
            var entryComponentId = cf.ComponentRef ?? componentId;

            var result = _database.InsertRow(
                "SELECT `Directory_`, `Component_` FROM `CreateFolder`",
                record => record
                    .SetString(1, cf.DirectoryRef)
                    .SetString(2, entryComponentId));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitMoveFiles(ResolvedPackage resolved)
    {
        var moveFiles = resolved.Package.MoveFiles;
        if (moveFiles.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var mf in moveFiles)
        {
            var entryComponentId = mf.ComponentRef ?? componentId;

            var result = _database.InsertRow(
                "SELECT `FileKey`, `Component_`, `SourceName`, `SourceFolder`, `DestName`, `DestFolder`, `Options` FROM `MoveFile`",
                record => record
                    .SetString(1, mf.Id)
                    .SetString(2, entryComponentId)
                    .SetString(3, mf.SourceFileName)
                    .SetString(4, mf.SourceDirectory)
                    .SetString(5, mf.DestFileName)
                    .SetString(6, mf.DestDirectory)
                    .SetInteger(7, mf.Options));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitDuplicateFiles(ResolvedPackage resolved)
    {
        var duplicateFiles = resolved.Package.DuplicateFiles;
        if (duplicateFiles.Count == 0) return Unit.Value;

        var componentId = resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";

        foreach (var df in duplicateFiles)
        {
            var entryComponentId = df.ComponentRef ?? componentId;

            var result = _database.InsertRow(
                "SELECT `FileKey`, `Component_`, `File_`, `DestFolder`, `DestName` FROM `DuplicateFile`",
                record => record
                    .SetString(1, df.Id)
                    .SetString(2, entryComponentId)
                    .SetString(3, df.FileRef)
                    .SetString(4, df.DestDirectory)
                    .SetString(5, df.DestFileName));
            if (result.IsFailure) return result;
        }
        return Unit.Value;
    }

    private Result<Unit> EmitCustomTables(ResolvedPackage resolved)
    {
        var customTables = resolved.Package.CustomTables;
        if (customTables.Count == 0) return Unit.Value;

        foreach (var table in customTables)
        {
            // Build CREATE TABLE SQL
            var columnDefs = new List<string>();
            var primaryKeys = new List<string>();
            foreach (var col in table.Columns)
            {
                var typeSql = col.Type switch
                {
                    CustomTableColumnType.Int16 => "SHORT",
                    CustomTableColumnType.Int32 => "LONG",
                    CustomTableColumnType.Binary or CustomTableColumnType.Stream => "OBJECT",
                    _ => $"CHAR({col.Width})"
                };
                var nullable = col.Nullable ? "" : " NOT NULL";
                columnDefs.Add($"`{col.Name}` {typeSql}{nullable}");
                if (col.PrimaryKey)
                    primaryKeys.Add($"`{col.Name}`");
            }

            var pkClause = primaryKeys.Count > 0 ? $" PRIMARY KEY {string.Join(", ", primaryKeys)}" : "";
            var createSql = $"CREATE TABLE `{table.Name}` ({string.Join(", ", columnDefs)}{pkClause})";

            var createResult = _database.Execute(createSql);
            if (createResult.IsFailure) return createResult;

            // Insert rows
            var selectColumns = string.Join(", ", table.Columns.Select(c => $"`{c.Name}`"));
            var selectSql = $"SELECT {selectColumns} FROM `{table.Name}`";

            foreach (var row in table.Rows)
            {
                var result = _database.InsertRow(selectSql, record =>
                {
                    uint colIndex = 1;
                    foreach (var col in table.Columns)
                    {
                        if (row.TryGetValue(col.Name, out var value) && value is not null)
                        {
                            if (col.Type is CustomTableColumnType.Int16 or CustomTableColumnType.Int32)
                                record.SetInteger(colIndex, Convert.ToInt32(value));
                            else
                                record.SetString(colIndex, value.ToString());
                        }
                        else
                        {
                            record.SetString(colIndex, null);
                        }
                        colIndex++;
                    }
                });
                if (result.IsFailure) return result;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> EmitAssemblies(ResolvedPackage resolved)
    {
        var assemblies = resolved.Package.Assemblies;
        if (assemblies.Count == 0) return Unit.Value;

        var defaultFeature = resolved.Package.Features.FirstOrDefault()?.Id ?? "Complete";

        foreach (var assembly in assemblies)
        {
            // Find the component that owns this file
            var component = resolved.Components.FirstOrDefault(c =>
                c.Files.Any(f => f.FileName.Equals(assembly.FileRef, StringComparison.OrdinalIgnoreCase)));
            var componentId = component?.Id ?? resolved.Components.FirstOrDefault()?.Id ?? "MainComponent";
            var featureId = component?.FeatureRef ?? defaultFeature;
            var attributes = (int)assembly.Type;

            // MsiAssembly row
            var result = _database.InsertRow(
                "SELECT `Component_`, `Feature_`, `File_Manifest`, `File_Application`, `Attributes` FROM `MsiAssembly`",
                record => record
                    .SetString(1, componentId)
                    .SetString(2, featureId)
                    .SetString(3, null)
                    .SetString(4, assembly.ApplicationFileRef)
                    .SetInteger(5, attributes));
            if (result.IsFailure) return result;

            // MsiAssemblyName rows for each non-null attribute
            if (!string.IsNullOrEmpty(assembly.AssemblyName))
            {
                result = InsertAssemblyNameRow(componentId, "name", assembly.AssemblyName);
                if (result.IsFailure) return result;
            }

            if (!string.IsNullOrEmpty(assembly.AssemblyVersion))
            {
                result = InsertAssemblyNameRow(componentId, "version", assembly.AssemblyVersion);
                if (result.IsFailure) return result;
            }

            if (!string.IsNullOrEmpty(assembly.AssemblyCulture))
            {
                result = InsertAssemblyNameRow(componentId, "culture", assembly.AssemblyCulture);
                if (result.IsFailure) return result;
            }

            if (!string.IsNullOrEmpty(assembly.AssemblyPublicKeyToken))
            {
                result = InsertAssemblyNameRow(componentId, "publicKeyToken", assembly.AssemblyPublicKeyToken);
                if (result.IsFailure) return result;
            }

            if (!string.IsNullOrEmpty(assembly.ProcessorArchitecture))
            {
                result = InsertAssemblyNameRow(componentId, "processorArchitecture", assembly.ProcessorArchitecture);
                if (result.IsFailure) return result;
            }
        }
        return Unit.Value;
    }

    private Result<Unit> InsertAssemblyNameRow(string componentId, string name, string value)
    {
        return _database.InsertRow(
            "SELECT `Component_`, `Name`, `Value` FROM `MsiAssemblyName`",
            record => record
                .SetString(1, componentId)
                .SetString(2, name)
                .SetString(3, value));
    }

    private static int GetSequenceForAction(string? after, string? before)
    {
        // Map well-known action names to sequence numbers and offset
        var knownActions = new Dictionary<string, int>
        {
            ["InstallInitialize"] = 1500,
            ["InstallFiles"] = 4000,
            ["InstallFinalize"] = 6600,
            ["WriteRegistryValues"] = 5000,
            ["CreateShortcuts"] = 4500,
            ["RemoveFiles"] = 3500,
        };

        if (after is not null && knownActions.TryGetValue(after, out var afterSeq))
            return afterSeq + 1;
        if (before is not null && knownActions.TryGetValue(before, out var beforeSeq))
            return beforeSeq - 1;

        return 4001; // Default: after InstallFiles
    }

    private static int GetRemoveExistingProductsSequence(RemoveExistingProductsSchedule schedule)
    {
        return schedule switch
        {
            RemoveExistingProductsSchedule.AfterInstallValidate => 1450,
            RemoveExistingProductsSchedule.AfterInstallInitialize => 1550,
            RemoveExistingProductsSchedule.AfterInstallExecute => 6500,
            RemoveExistingProductsSchedule.AfterInstallExecuteAgain => 6550,
            RemoveExistingProductsSchedule.AfterInstallFinalize => 6650,
            _ => 1450
        };
    }

    private Result<Unit> InsertDirectoryRow(string id, string? parentId, string defaultDir)
    {
        return _database.InsertRow(
            "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`",
            record => record
                .SetString(1, id)
                .SetString(2, parentId)
                .SetString(3, defaultDir));
    }

    private Result<Unit> InsertPropertyRow(string name, string value)
    {
        return _database.InsertRow(
            "SELECT `Property`, `Value` FROM `Property`",
            record => record
                .SetString(1, name)
                .SetString(2, value));
    }

    private Result<Unit> InsertLaunchConditionRow(string condition, string description)
    {
        return _database.InsertRow(
            "SELECT `Condition`, `Description` FROM `LaunchCondition`",
            record => record
                .SetString(1, condition)
                .SetString(2, description));
    }

    private static string GetDirectoryId(InstallPath path)
    {
        var segments = path.Segments;
        if (segments.Count == 0) return path.Root.Token;

        var parentId = path.Root.Token;
        for (var i = 0; i < segments.Count; i++)
        {
            parentId = $"D_{SanitizeId(segments[i])}_{StableHash(parentId)}";
            if (parentId.Length > 72) parentId = parentId[..72];
        }
        return parentId;
    }

    private static string SanitizeId(string name)
    {
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }
        return new string(sanitized);
    }

    private static string StableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 4); // 8 hex chars, deterministic across runtimes
    }

    private static string GetShortFileName(string longName)
    {
        // Generate 8.3 short name
        var name = Path.GetFileNameWithoutExtension(longName);
        var ext = Path.GetExtension(longName);

        if (name.Length <= 8 && ext.Length <= 4 && !name.Contains(' '))
            return longName;

        var shortName = name.Replace(" ", "").Replace(".", "");
        if (shortName.Length > 6) shortName = shortName[..6] + "~1";
        var shortExt = ext.Length > 4 ? ext[..4] : ext;

        return $"{shortName}{shortExt}".ToUpperInvariant();
    }
}
