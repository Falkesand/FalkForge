namespace FalkForge.Compiler.Msix;

public static class MsixValidator
{
    public static Result<Unit> Validate(MsixModel model)
    {
        // MSIX001: Package Name is required
        if (string.IsNullOrWhiteSpace(model.Name))
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX001: Package Name is required.");

        // MSIX002: Publisher is required
        if (string.IsNullOrWhiteSpace(model.Publisher))
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX002: Publisher is required.");

        // MSIX003: Publisher must start with 'CN='
        if (!model.Publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX003: Publisher must start with 'CN=' (certificate subject format).");

        // MSIX004: Version must have 4 parts
        if (model.Version.Revision < 0)
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX004: Version must have 4 parts (Major.Minor.Build.Revision).");

        // MSIX005: At least one Application is required
        if (model.Applications.Count == 0)
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX005: At least one Application is required.");

        // MSIX006: DisplayName is required
        if (string.IsNullOrWhiteSpace(model.DisplayName))
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX006: DisplayName is required.");

        // MSIX007: PublisherDisplayName is required
        if (string.IsNullOrWhiteSpace(model.PublisherDisplayName))
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX007: PublisherDisplayName is required.");

        // MSIX008: MSIX packages must be signed
        if (model.Signing is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "MSIX008: MSIX packages must be signed. Provide SigningOptions.");

        // MSIX010: Application.Id is required (for each app)
        // MSIX011: Application.Executable is required (for each app)
        foreach (var app in model.Applications)
        {
            if (string.IsNullOrWhiteSpace(app.Id))
                return Result<Unit>.Failure(ErrorKind.Validation, "MSIX010: Application Id is required.");
            if (string.IsNullOrWhiteSpace(app.Executable))
                return Result<Unit>.Failure(ErrorKind.Validation, "MSIX011: Application Executable is required.");
        }

        // MSIX012: MinWindowsVersion must be valid
        if (!System.Version.TryParse(model.MinWindowsVersion, out _))
            return Result<Unit>.Failure(ErrorKind.Validation, $"MSIX012: Invalid MinWindowsVersion: {model.MinWindowsVersion}");

        return Result<Unit>.Success(Unit.Value);
    }
}
