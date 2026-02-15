namespace FalkForge.Compiler.Bundle.Validation;

public sealed class BundleValidator
{
    public Result<Unit> Validate(BundleModel model)
    {
        // BDL001: Name is required
        if (string.IsNullOrWhiteSpace(model.Name))
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL001: Bundle name is required");

        // BDL002: Manufacturer is required
        if (string.IsNullOrWhiteSpace(model.Manufacturer))
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL002: Bundle manufacturer is required");

        // BDL003: Version must be valid
        if (!System.Version.TryParse(model.Version, out _))
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL003: Invalid version format: {model.Version}");

        // BDL004: At least one package required
        if (model.Packages.Count == 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL004: Bundle must contain at least one package");

        // BDL005: Package IDs must be unique
        var duplicateIds = model.Packages
            .GroupBy(p => p.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL005: Duplicate package IDs: {string.Join(", ", duplicateIds)}");

        return Unit.Value;
    }
}
