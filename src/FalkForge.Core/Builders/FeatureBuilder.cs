using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class FeatureBuilder
{
    private readonly List<FeatureBuilder> _childBuilders = [];
    private readonly List<FeatureConditionModel> _conditions = [];
    private readonly List<FileEntryModel> _files = [];
    private readonly List<ServiceModel> _services = [];
    private readonly List<RegistryEntryModel> _registryEntries = [];
    private readonly List<ShortcutModel> _shortcuts = [];
    private readonly List<EnvironmentVariableModel> _environmentVariables = [];
    private readonly List<FontModel> _fonts = [];
    private readonly List<IniFileModel> _iniFiles = [];
    private readonly List<PermissionModel> _permissions = [];
    private readonly List<FileAssociationModel> _fileAssociations = [];
    private readonly string _id;

    internal FeatureBuilder(string id)
    {
        _id = id;
    }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefault { get; set; } = true;
    public int DisplayLevel { get; set; } = 1;

    public FeatureBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        _childBuilders.Add(builder);
        return this;
    }

    public FeatureBuilder Condition(string condition, int level)
    {
        _conditions.Add(new FeatureConditionModel { Condition = condition, Level = level });
        return this;
    }

    public FeatureBuilder Condition(string condition)
    {
        return Condition(condition, 0);
    }

    public FeatureBuilder Condition(Condition condition, int level)
    {
        return Condition(condition.ToString(), level);
    }

    public FeatureBuilder Condition(Condition condition)
    {
        return Condition(condition.ToString(), 0);
    }

    public FeatureBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares a Windows service scoped to this feature. Mirrors <see cref="Files"/>:
    /// the resulting <see cref="ServiceModel"/> is stamped with this feature's ID so the
    /// compiler places the service's synthesized component under the correct MSI feature.
    /// </summary>
    public FeatureBuilder Service(string name, Action<ServiceBuilder> configure)
    {
        var builder = new ServiceBuilder(name);
        configure(builder);
        _services.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares registry entries scoped to this feature. Mirrors <see cref="Files"/>:
    /// the resulting <see cref="RegistryEntryModel"/> rows are stamped with this feature's ID
    /// so the compiler places their synthesized component under the correct MSI feature.
    /// </summary>
    public FeatureBuilder Registry(Action<RegistryBuilder> configure)
    {
        var builder = new RegistryBuilder();
        configure(builder);
        _registryEntries.AddRange(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares a shortcut scoped to this feature. Mirrors <see cref="Files"/>: the resulting
    /// <see cref="ShortcutModel"/> is stamped with this feature's ID so the compiler places the
    /// shortcut's synthesized component under the correct MSI feature.
    /// </summary>
    public ShortcutBuilder Shortcut(string name, string targetFile)
    {
        return new ShortcutBuilder(name, targetFile, _shortcuts.Add);
    }

    /// <summary>
    /// Declares an environment variable scoped to this feature. Mirrors <see cref="Files"/>:
    /// the resulting <see cref="EnvironmentVariableModel"/> is stamped with this feature's ID so
    /// the compiler places its synthesized component under the correct MSI feature.
    /// </summary>
    public FeatureBuilder EnvironmentVariable(string name, string value,
        Action<EnvironmentVariableBuilder>? configure = null)
    {
        var builder = new EnvironmentVariableBuilder(name, value);
        configure?.Invoke(builder);
        _environmentVariables.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares a font scoped to this feature. Mirrors <see cref="Files"/> for API symmetry: the
    /// resulting <see cref="FontModel"/> is stamped with this feature's ID. Unlike the other five
    /// entry points, the real MSI <c>Font</c> table has no Component_/Feature_ column of its own —
    /// a Font row always rides on the File it references, so the actual feature placement for a
    /// font is governed by declaring its source file inside this same feature scope via
    /// <see cref="Files"/>, not by this stamped FeatureRef (which no producer reads).
    /// </summary>
    public FeatureBuilder Font(string fileName, Action<FontBuilder>? configure = null)
    {
        var builder = new FontBuilder(fileName);
        configure?.Invoke(builder);
        _fonts.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares an INI file entry scoped to this feature. Mirrors <see cref="Files"/>: the
    /// resulting <see cref="IniFileModel"/> is stamped with this feature's ID so the compiler
    /// places its synthesized component under the correct MSI feature.
    /// </summary>
    public FeatureBuilder IniFile(string fileName, Action<IniFileBuilder> configure)
    {
        var builder = new IniFileBuilder(fileName);
        configure(builder);
        _iniFiles.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares an NTFS/registry/folder permission scoped to this feature. Mirrors
    /// <see cref="Files"/>: the resulting <see cref="PermissionModel"/> is stamped with this
    /// feature's ID. Only SDDL-driven permissions (routed to <c>MsiLockPermissionsEx</c>) can
    /// honor this — that table has a <c>Condition</c> column the compiler encodes as
    /// <c>&amp;FeatureId=3</c>. User/Domain-driven permissions route to <c>LockPermissions</c>,
    /// which has no Condition or Component column at all, so gating one fails the compile instead
    /// of silently doing nothing (see PRM005).
    /// </summary>
    public FeatureBuilder Permission(string lockObject, Action<PermissionBuilder> configure)
    {
        var builder = new PermissionBuilder(lockObject);
        configure(builder);
        _permissions.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Declares a file association scoped to this feature. Mirrors <see cref="Files"/>: the
    /// resulting <see cref="FileAssociationModel"/> is stamped with this feature's ID so the
    /// compiler places its synthesized component under the correct MSI feature and sets the
    /// <c>Extension</c> table's own <c>Feature_</c> column to match.
    /// </summary>
    public FeatureBuilder FileAssociation(string extension, Action<FileAssociationBuilder> configure)
    {
        var builder = new FileAssociationBuilder(extension);
        configure(builder);
        _fileAssociations.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Collects all files declared on this feature and its nested child features,
    /// each stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift files into the flat PackageModel.Files list.
    /// </summary>
    internal IReadOnlyList<FileEntryModel> CollectFiles()
    {
        var result = new List<FileEntryModel>(_files.Count);

        foreach (var file in _files)
            result.Add(new FileEntryModel
            {
                SourcePath = file.SourcePath,
                TargetDirectory = file.TargetDirectory,
                FileName = file.FileName,
                IsKeyPath = file.IsKeyPath,
                ComponentId = file.ComponentId,
                ComponentGuid = file.ComponentGuid,
                FeatureRef = _id,
                Vital = file.Vital,
                NeverOverwrite = file.NeverOverwrite,
                Permanent = file.Permanent,
                ComponentCondition = file.ComponentCondition
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectFiles());

        return result;
    }

    /// <summary>
    /// Collects all services declared on this feature and its nested child features,
    /// each stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift services into the flat PackageModel.Services list.
    /// </summary>
    internal IReadOnlyList<ServiceModel> CollectServices()
    {
        var result = new List<ServiceModel>(_services.Count);

        foreach (var service in _services)
            result.Add(new ServiceModel
            {
                Name = service.Name,
                DisplayName = service.DisplayName,
                Executable = service.Executable,
                Description = service.Description,
                StartMode = service.StartMode,
                Account = service.Account,
                UserName = service.UserName,
                Password = service.Password,
                Arguments = service.Arguments,
                AccountProperty = service.AccountProperty,
                ComponentCondition = service.ComponentCondition,
                Dependencies = service.Dependencies,
                TypedDependencies = service.TypedDependencies,
                FailureActions = service.FailureActions,
                Permissions = service.Permissions,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectServices());

        return result;
    }

    /// <summary>
    /// Collects all registry entries declared on this feature and its nested child features,
    /// each stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift entries into the flat PackageModel.RegistryEntries list.
    /// </summary>
    internal IReadOnlyList<RegistryEntryModel> CollectRegistryEntries()
    {
        var result = new List<RegistryEntryModel>(_registryEntries.Count);

        foreach (var entry in _registryEntries)
            result.Add(new RegistryEntryModel
            {
                Root = entry.Root,
                Key = entry.Key,
                ValueName = entry.ValueName,
                Value = entry.Value,
                ValueType = entry.ValueType,
                FeatureRef = _id,
                ComponentId = entry.ComponentId
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectRegistryEntries());

        return result;
    }

    /// <summary>
    /// Collects all shortcuts declared on this feature and its nested child features, each
    /// stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift shortcuts into the flat PackageModel.Shortcuts list.
    /// </summary>
    internal IReadOnlyList<ShortcutModel> CollectShortcuts()
    {
        var result = new List<ShortcutModel>(_shortcuts.Count);

        foreach (var shortcut in _shortcuts)
            result.Add(new ShortcutModel
            {
                Name = shortcut.Name,
                TargetFile = shortcut.TargetFile,
                Locations = shortcut.Locations,
                WorkingDirectory = shortcut.WorkingDirectory,
                Arguments = shortcut.Arguments,
                Description = shortcut.Description,
                IconFile = shortcut.IconFile,
                IconIndex = shortcut.IconIndex,
                StartMenuSubfolder = shortcut.StartMenuSubfolder,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectShortcuts());

        return result;
    }

    /// <summary>
    /// Collects all environment variables declared on this feature and its nested child features,
    /// each stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift entries into the flat PackageModel.EnvironmentVariables list.
    /// </summary>
    internal IReadOnlyList<EnvironmentVariableModel> CollectEnvironmentVariables()
    {
        var result = new List<EnvironmentVariableModel>(_environmentVariables.Count);

        foreach (var envVar in _environmentVariables)
            result.Add(new EnvironmentVariableModel
            {
                Name = envVar.Name,
                Value = envVar.Value,
                IsSystem = envVar.IsSystem,
                Action = envVar.Action,
                Part = envVar.Part,
                Separator = envVar.Separator,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectEnvironmentVariables());

        return result;
    }

    /// <summary>
    /// Collects all fonts declared on this feature and its nested child features, each stamped
    /// with a FeatureRef pointing to their owning feature ID. See <see cref="Font"/> for why this
    /// stamped FeatureRef is metadata-only — no producer reads it.
    /// Called by PackageBuilder.Feature() to lift fonts into the flat PackageModel.Fonts list.
    /// </summary>
    internal IReadOnlyList<FontModel> CollectFonts()
    {
        var result = new List<FontModel>(_fonts.Count);

        foreach (var font in _fonts)
            result.Add(new FontModel
            {
                FileName = font.FileName,
                FontTitle = font.FontTitle,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectFonts());

        return result;
    }

    /// <summary>
    /// Collects all INI file entries declared on this feature and its nested child features, each
    /// stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift entries into the flat PackageModel.IniFiles list.
    /// </summary>
    internal IReadOnlyList<IniFileModel> CollectIniFiles()
    {
        var result = new List<IniFileModel>(_iniFiles.Count);

        foreach (var ini in _iniFiles)
            result.Add(new IniFileModel
            {
                FileName = ini.FileName,
                DirProperty = ini.DirProperty,
                Section = ini.Section,
                Key = ini.Key,
                Value = ini.Value,
                Action = ini.Action,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectIniFiles());

        return result;
    }

    /// <summary>
    /// Collects all permissions declared on this feature and its nested child features, each
    /// stamped with a FeatureRef pointing to their owning feature ID. See <see cref="Permission"/>
    /// for how (and how far) this FeatureRef is honored at compile time.
    /// Called by PackageBuilder.Feature() to lift entries into the flat PackageModel.Permissions list.
    /// </summary>
    internal IReadOnlyList<PermissionModel> CollectPermissions()
    {
        var result = new List<PermissionModel>(_permissions.Count);

        foreach (var perm in _permissions)
            result.Add(new PermissionModel
            {
                LockObject = perm.LockObject,
                Table = perm.Table,
                Sddl = perm.Sddl,
                Domain = perm.Domain,
                User = perm.User,
                Permission = perm.Permission,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectPermissions());

        return result;
    }

    /// <summary>
    /// Collects all file associations declared on this feature and its nested child features,
    /// each stamped with a FeatureRef pointing to their owning feature ID.
    /// Called by PackageBuilder.Feature() to lift entries into the flat PackageModel.FileAssociations list.
    /// </summary>
    internal IReadOnlyList<FileAssociationModel> CollectFileAssociations()
    {
        var result = new List<FileAssociationModel>(_fileAssociations.Count);

        foreach (var assoc in _fileAssociations)
            result.Add(new FileAssociationModel
            {
                Extension = assoc.Extension,
                ProgId = assoc.ProgId,
                Description = assoc.Description,
                IconFile = assoc.IconFile,
                IconIndex = assoc.IconIndex,
                ContentType = assoc.ContentType,
                Verbs = assoc.Verbs,
                FeatureRef = _id
            });

        foreach (var child in _childBuilders)
            result.AddRange(child.CollectFileAssociations());

        return result;
    }

    internal FeatureModel Build()
    {
        return new FeatureModel
        {
            Id = _id,
            Title = string.IsNullOrEmpty(Title) ? _id : Title,
            Description = Description,
            IsRequired = IsRequired,
            IsDefault = IsDefault,
            DisplayLevel = DisplayLevel,
            Children = [.. _childBuilders.Select(b => b.Build())],
            Conditions = _conditions
        };
    }
}