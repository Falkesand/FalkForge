namespace FalkInstaller.Extensions.DotNet;

public static class DotNetSearchValidator
{
    public static Result<Unit> Validate(DotNetCoreSearchModel model)
    {
        if (string.IsNullOrWhiteSpace(model.VariableName))
            return Result<Unit>.Failure(ErrorKind.Validation, "NET001: VariableName is required.");

        if (model.MinimumVersion is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "NET002: MinimumVersion is required.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAll(IReadOnlyList<DotNetCoreSearchModel> models)
    {
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            var result = Validate(model);
            if (result.IsFailure)
                return result;

            if (!string.IsNullOrWhiteSpace(model.VariableName) && !variables.Add(model.VariableName))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"NET003: Duplicate VariableName '{model.VariableName}'.");
        }

        return Unit.Value;
    }
}
