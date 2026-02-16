namespace FalkForge.Extensions.Util.XmlConfig;

public static class XmlConfigValidator
{
    private const int MaxXPathLength = 4096;

    public static Result<Unit> Validate(XmlConfigModel model)
    {
        if (string.IsNullOrWhiteSpace(model.XPath))
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF001: XPath expression must not be empty.");

        if (string.IsNullOrWhiteSpace(model.FilePath))
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF002: FilePath must not be empty.");

        if (model.Action == XmlConfigAction.CreateElement && string.IsNullOrWhiteSpace(model.ElementName))
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF003: CreateElement action requires ElementName.");

        if (model.Action == XmlConfigAction.SetAttribute &&
            (string.IsNullOrWhiteSpace(model.AttributeName) || model.Value is null))
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF004: SetAttribute action requires AttributeName and Value.");

        if (model.XPath.Length > MaxXPathLength)
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"XCF005: XPath expression exceeds maximum length of {MaxXPathLength} characters.");

        if (model.Action == XmlConfigAction.DeleteAttribute && string.IsNullOrWhiteSpace(model.AttributeName))
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF006: DeleteAttribute action requires AttributeName.");

        if (model.Action == XmlConfigAction.SetValue && model.Value is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF007: SetValue action requires Value.");

        if (model.Action == XmlConfigAction.BulkSetValue && model.Value is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "XCF008: BulkSetValue action requires Value.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAll(IReadOnlyList<XmlConfigModel> models)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (!string.IsNullOrWhiteSpace(model.Id) && !ids.Add(model.Id))
                return Result<Unit>.Failure(ErrorKind.Validation, $"XCF009: Duplicate XmlConfig Id '{model.Id}'.");

            var result = Validate(model);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }
}
