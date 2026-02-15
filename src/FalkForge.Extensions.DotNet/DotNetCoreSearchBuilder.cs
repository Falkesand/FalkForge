namespace FalkForge.Extensions.DotNet;

public sealed class DotNetCoreSearchBuilder
{
    private DotNetRuntimeType _runtimeType;
    private DotNetPlatform _platform;
    private Version? _minimumVersion;
    private string? _variableName;

    public DotNetCoreSearchBuilder RuntimeType(DotNetRuntimeType runtimeType)
    {
        _runtimeType = runtimeType;
        return this;
    }

    public DotNetCoreSearchBuilder Platform(DotNetPlatform platform)
    {
        _platform = platform;
        return this;
    }

    public DotNetCoreSearchBuilder MinVersion(Version minimumVersion)
    {
        _minimumVersion = minimumVersion;
        return this;
    }

    public DotNetCoreSearchBuilder Variable(string variableName)
    {
        _variableName = variableName;
        return this;
    }

    public Result<DotNetCoreSearchModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_variableName))
            return Result<DotNetCoreSearchModel>.Failure(ErrorKind.Validation, "NET001: VariableName is required.");

        if (_minimumVersion is null)
            return Result<DotNetCoreSearchModel>.Failure(ErrorKind.Validation, "NET002: MinimumVersion is required.");

        var model = new DotNetCoreSearchModel
        {
            RuntimeType = _runtimeType,
            Platform = _platform,
            MinimumVersion = _minimumVersion,
            VariableName = _variableName
        };

        var validationResult = DotNetSearchValidator.Validate(model);
        if (validationResult.IsFailure)
            return Result<DotNetCoreSearchModel>.Failure(validationResult.Error);

        return model;
    }
}
