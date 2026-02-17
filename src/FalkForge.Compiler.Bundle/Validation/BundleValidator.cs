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

        // BDL008: BundleId must not be empty
        if (model.BundleId == Guid.Empty)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL008: BundleId must not be empty GUID");

        // BDL009: UpgradeCode must not be empty
        if (model.UpgradeCode == Guid.Empty)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL009: UpgradeCode must not be empty GUID");

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

        // BDL006: Package container references must resolve to defined containers
        var containerIds = new HashSet<string>(model.Containers.Select(c => c.Id));
        var invalidRefs = model.Packages
            .Where(p => p.ContainerId is not null && !containerIds.Contains(p.ContainerId))
            .Select(p => $"Package '{p.Id}' references undefined container '{p.ContainerId}'")
            .ToArray();
        if (invalidRefs.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL006: {string.Join("; ", invalidRefs)}");

        // BDL007: Custom UI requires a project path
        if (model.UiConfig is { UiType: BundleUiType.Custom } uiConfig &&
            string.IsNullOrWhiteSpace(uiConfig.CustomUiProjectPath))
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL007: Custom UI requires a project path (CustomUiProjectPath)");

        // BDL010: Variable name is required
        foreach (var variable in model.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL010: Variable name is required");
        }

        // BDL011: Variable names must be unique
        var duplicateVarNames = model.Variables
            .GroupBy(v => v.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateVarNames.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL011: Duplicate variable names: {string.Join(", ", duplicateVarNames)}");

        // BDL012: Default value must match variable type
        foreach (var variable in model.Variables)
        {
            if (variable.DefaultValue is null)
                continue;

            if (variable.Type == BundleVariableType.Numeric && !long.TryParse(variable.DefaultValue, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL012: Variable '{variable.Name}' has type Numeric but default value '{variable.DefaultValue}' is not a valid integer");

            if (variable.Type == BundleVariableType.Version && !System.Version.TryParse(variable.DefaultValue, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL012: Variable '{variable.Name}' has type Version but default value '{variable.DefaultValue}' is not a valid version");
        }

        // BDL013: Secret variable cannot be Persisted
        foreach (var variable in model.Variables)
        {
            if (variable.Secret && variable.Persisted)
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL013: Variable '{variable.Name}' is Secret and cannot be Persisted");
        }

        return Unit.Value;
    }
}
