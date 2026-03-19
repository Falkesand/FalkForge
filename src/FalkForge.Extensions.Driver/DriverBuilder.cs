namespace FalkForge.Extensions.Driver;

public sealed class DriverBuilder
{
    private string _id = string.Empty;
    private string _infFilePath = string.Empty;
    private DriverInstallFlags _flags;
    private string? _description;
    private string? _condition;

    public DriverBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public DriverBuilder InfFilePath(string infFilePath)
    {
        _infFilePath = infFilePath;
        return this;
    }

    public DriverBuilder Force()
    {
        _flags |= DriverInstallFlags.ForceInstall;
        return this;
    }

    public DriverBuilder PlugAndPlay()
    {
        _flags |= DriverInstallFlags.PlugAndPlay;
        return this;
    }

    public DriverBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public DriverBuilder Condition(string condition)
    {
        _condition = condition;
        return this;
    }

    internal Result<DriverModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<DriverModel>.Failure(ErrorKind.Validation, "DRV001: Driver Id must not be empty.");

        if (string.IsNullOrWhiteSpace(_infFilePath))
            return Result<DriverModel>.Failure(ErrorKind.Validation, "DRV002: Driver InfFilePath must not be empty.");

        if (!_infFilePath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            return Result<DriverModel>.Failure(ErrorKind.Validation, "DRV003: Driver InfFilePath must end with '.inf'.");

        return Result<DriverModel>.Success(new DriverModel
        {
            Id = _id,
            InfFilePath = _infFilePath,
            Flags = _flags,
            Description = _description,
            Condition = _condition,
        });
    }
}
