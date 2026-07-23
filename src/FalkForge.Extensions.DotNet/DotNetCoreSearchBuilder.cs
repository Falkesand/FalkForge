namespace FalkForge.Extensions.DotNet;

public sealed class DotNetCoreSearchBuilder
{
    private string? _message;
    private Version? _minimumVersion;
    private DotNetPlatform _platform;
    private DotNetRuntimeType _runtimeType;
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

    /// <summary>
    ///     Sets the <c>LaunchCondition</c> message the DotNet extension itself emits for this search
    ///     (the JSON authoring path's shape). Leave unset when the author gates via
    ///     <c>PackageBuilder.Require(variableName, message)</c> instead (the C# fluent authoring path) —
    ///     see <see cref="DotNetCoreSearchModel.Message"/>.
    /// </summary>
    public DotNetCoreSearchBuilder Message(string message)
    {
        _message = message;
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
            VariableName = _variableName,
            Message = _message
        };

        var validationResult = DotNetSearchValidator.Validate(model);
        if (validationResult.IsFailure)
            return Result<DotNetCoreSearchModel>.Failure(validationResult.Error);

        return model;
    }
}