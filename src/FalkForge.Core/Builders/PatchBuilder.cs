namespace FalkForge.Builders;

using FalkForge.Models;
using FalkForge.Validation;

public sealed class PatchBuilder
{
    private Guid _id;
    private PatchClassification _classification = PatchClassification.Update;
    private string? _description;
    private string? _manufacturer;
    private Guid _targetProductCode;
    private string? _targetVersion;
    private string? _updatedVersion;
    private string _targetMsiPath = string.Empty;
    private string _updatedMsiPath = string.Empty;
    private bool _allowRemoval;

    public PatchBuilder Id(Guid id)
    {
        _id = id;
        return this;
    }

    public PatchBuilder Classification(PatchClassification classification)
    {
        _classification = classification;
        return this;
    }

    public PatchBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public PatchBuilder Manufacturer(string manufacturer)
    {
        _manufacturer = manufacturer;
        return this;
    }

    public PatchBuilder TargetProduct(Guid productCode)
    {
        _targetProductCode = productCode;
        return this;
    }

    public PatchBuilder TargetVersion(string version)
    {
        _targetVersion = version;
        return this;
    }

    public PatchBuilder UpdatedVersion(string version)
    {
        _updatedVersion = version;
        return this;
    }

    public PatchBuilder TargetMsi(string path)
    {
        _targetMsiPath = path;
        return this;
    }

    public PatchBuilder UpdatedMsi(string path)
    {
        _updatedMsiPath = path;
        return this;
    }

    public PatchBuilder AllowRemoval(bool allow = true)
    {
        _allowRemoval = allow;
        return this;
    }

    public Result<PatchModel> Build()
    {
        var model = new PatchModel
        {
            Id = _id,
            Classification = _classification,
            Description = _description,
            Manufacturer = _manufacturer,
            TargetProductCode = _targetProductCode,
            TargetVersion = _targetVersion,
            UpdatedVersion = _updatedVersion,
            TargetMsiPath = _targetMsiPath,
            UpdatedMsiPath = _updatedMsiPath,
            AllowRemoval = _allowRemoval
        };

        var validation = PatchValidator.Validate(model);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
            return Result<PatchModel>.Failure(ErrorKind.Validation, errors);
        }

        return model;
    }
}
