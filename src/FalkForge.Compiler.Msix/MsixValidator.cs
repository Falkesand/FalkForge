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

            var extensionResult = ValidateExtensions(app);
            if (extensionResult.IsFailure)
                return extensionResult;
        }

        // MSIX012: MinWindowsVersion must be valid
        if (!System.Version.TryParse(model.MinWindowsVersion, out _))
            return Result<Unit>.Failure(ErrorKind.Validation, $"MSIX012: Invalid MinWindowsVersion: {model.MinWindowsVersion}");

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Validates the application's declared extensions against the AppxManifest rules that
    /// MakeAppx and the Store enforce, so a bad association fails the build here rather than
    /// as an opaque packaging or deployment error later.
    /// </summary>
    private static Result<Unit> ValidateExtensions(MsixApplication app)
    {
        foreach (var fta in app.FileTypeAssociations)
        {
            // MSIX013: uap:FileTypeAssociation/@Name allows lowercase alphanumerics, '.', '-', '_'.
            if (string.IsNullOrWhiteSpace(fta.Name) || !IsValidAssociationName(fta.Name))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"MSIX013: File type association name '{fta.Name}' is invalid. Use lowercase letters, digits, '.', '-' or '_'.");

            // MSIX014: uap:SupportedFileTypes requires at least one uap:FileType child.
            if (fta.FileTypes.Count == 0)
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"MSIX014: File type association '{fta.Name}' declares no file types.");

            foreach (var fileType in fta.FileTypes)
            {
                if (string.IsNullOrWhiteSpace(fileType) || fileType[0] != '.' || fileType.Length < 2)
                    return Result<Unit>.Failure(ErrorKind.Validation,
                        $"MSIX014: File type '{fileType}' in association '{fta.Name}' must include the leading dot (e.g. '.cdoc').");

                if (!IsLowerInvariant(fileType))
                    return Result<Unit>.Failure(ErrorKind.Validation,
                        $"MSIX014: File type '{fileType}' in association '{fta.Name}' must be lowercase.");
            }
        }

        foreach (var protocol in app.Protocols)
        {
            // MSIX015: the Name is the URI scheme itself — lowercase, letter-led, no separator.
            if (string.IsNullOrWhiteSpace(protocol.Name) || !IsValidProtocolName(protocol.Name))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"MSIX015: Protocol name '{protocol.Name}' is invalid. Use the scheme alone in lowercase (e.g. 'contoso', not 'contoso://').");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool IsValidAssociationName(string name)
    {
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_'))
                return false;
        }
        return true;
    }

    private static bool IsValidProtocolName(string name)
    {
        // RFC 3986 scheme grammar, further restricted to lowercase by the AppX schema.
        if (!char.IsAsciiLetterLower(name[0]))
            return false;

        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '+'))
                return false;
        }
        return true;
    }

    private static bool IsLowerInvariant(string value)
    {
        foreach (var c in value)
        {
            if (char.IsAsciiLetterUpper(c))
                return false;
        }
        return true;
    }
}
