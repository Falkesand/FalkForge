using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Builders;

public sealed class TransformBuilder
{
    private readonly Dictionary<string, string> _propertyChanges = new();
    private string _baseMsiPath = string.Empty;
    private string? _description;
    private string? _id;
    private string _targetMsiPath = string.Empty;

    public TransformBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public TransformBuilder BaseMsi(string path)
    {
        _baseMsiPath = path;
        return this;
    }

    public TransformBuilder TargetMsi(string path)
    {
        _targetMsiPath = path;
        return this;
    }

    public TransformBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public TransformBuilder SetProperty(string name, string value)
    {
        _propertyChanges[name] = value;
        return this;
    }

    public Result<TransformModel> Build()
    {
        var model = new TransformModel
        {
            Id = _id,
            BaseMsiPath = _baseMsiPath,
            TargetMsiPath = _targetMsiPath,
            Description = _description,
            PropertyChanges = _propertyChanges
        };

        var check = TransformValidator.Check(model);
        if (check.IsFailure)
            return Result<TransformModel>.Failure(check.Error);

        return model;
    }
}