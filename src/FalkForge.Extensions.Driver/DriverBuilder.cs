namespace FalkForge.Extensions.Driver;

public sealed class DriverBuilder
{
    private string _id = string.Empty;
    private string _infFilePath = string.Empty;
    private bool _forceInstall;
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

    public DriverBuilder ForceInstall(bool force = true)
    {
        _forceInstall = force;
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

        return Result<DriverModel>.Success(new DriverModel
        {
            Id = _id,
            InfFilePath = _infFilePath,
            ForceInstall = _forceInstall,
            Condition = _condition
        });
    }
}
