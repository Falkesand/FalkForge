using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class FeatureBuilder
{
    private readonly List<FeatureBuilder> _childBuilders = [];
    private readonly List<FeatureConditionModel> _conditions = [];
    private readonly List<FileEntryModel> _files = [];
    private readonly List<ServiceModel> _services = [];
    private readonly List<RegistryEntryModel> _registryEntries = [];
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