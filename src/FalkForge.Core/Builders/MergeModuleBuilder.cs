using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Builders;

public sealed class MergeModuleBuilder
{
    private readonly List<string> _components = [];
    private readonly List<string> _dependencies = [];
    private string? _description;
    private Guid _id;
    private int _language = 1033;
    private string _manufacturer = string.Empty;
    private Version _version = new(1, 0, 0);

    public MergeModuleBuilder Id(Guid id)
    {
        _id = id;
        return this;
    }

    public MergeModuleBuilder Language(int language)
    {
        _language = language;
        return this;
    }

    public MergeModuleBuilder Version(Version version)
    {
        _version = version;
        return this;
    }

    public MergeModuleBuilder Manufacturer(string manufacturer)
    {
        _manufacturer = manufacturer;
        return this;
    }

    public MergeModuleBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public MergeModuleBuilder Component(string componentId)
    {
        _components.Add(componentId);
        return this;
    }

    public MergeModuleBuilder Dependency(string dependencyId)
    {
        _dependencies.Add(dependencyId);
        return this;
    }

    public Result<MergeModuleModel> Build()
    {
        var model = new MergeModuleModel
        {
            Id = _id,
            Language = _language,
            Version = _version,
            Manufacturer = _manufacturer,
            Description = _description,
            Components = _components,
            Dependencies = _dependencies
        };

        var check = MergeModuleValidator.Check(model);
        if (check.IsFailure)
            return Result<MergeModuleModel>.Failure(check.Error);

        return model;
    }
}