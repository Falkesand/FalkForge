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

        // BDL014: Feature ID is required
        foreach (var feature in model.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Id))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL014: Feature ID is required");
        }

        // BDL018: Feature title is required
        foreach (var feature in model.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Title))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL018: Feature '{feature.Id}' title is required");
        }

        // BDL015: Feature IDs must be unique
        var duplicateFeatureIds = model.Features
            .GroupBy(f => f.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateFeatureIds.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL015: Duplicate feature IDs: {string.Join(", ", duplicateFeatureIds)}");

        // BDL016: Feature package references must resolve to defined packages
        var packageIds = new HashSet<string>(
            model.Chain
                .OfType<PackageChainItem>()
                .Select(ci => ci.Package.Id));

        foreach (var feature in model.Features)
        {
            var unknownPackages = feature.PackageIds
                .Where(pid => !packageIds.Contains(pid))
                .ToArray();

            if (unknownPackages.Length > 0)
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL016: Feature '{feature.Id}' references unknown package IDs: {string.Join(", ", unknownPackages)}");
        }

        // BDL017: Required feature must have at least one package
        foreach (var feature in model.Features)
        {
            if (feature.IsRequired && feature.PackageIds.Count == 0)
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL017: Required feature '{feature.Id}' has no packages");
        }

        // BDL019: Dependency provider key must not be empty
        foreach (var provider in model.DependencyProviders)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL019: Dependency provider key must not be empty.");

            // BDL020: Dependency provider version must be valid
            if (!string.IsNullOrWhiteSpace(provider.Version) && !System.Version.TryParse(provider.Version, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL020: Dependency provider '{provider.Key}' has invalid version '{provider.Version}'.");
        }

        // BDL021: Duplicate dependency provider keys
        var duplicateProviderKeys = model.DependencyProviders
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .GroupBy(p => p.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateProviderKeys.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL021: Duplicate dependency provider key '{string.Join(", ", duplicateProviderKeys)}'.");

        // BDL022/BDL023: Dependency consumer validation
        foreach (var consumer in model.DependencyConsumers)
        {
            if (string.IsNullOrWhiteSpace(consumer.ProviderKey))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL022: Dependency consumer provider key must not be empty.");
            if (string.IsNullOrWhiteSpace(consumer.ConsumerKey))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL023: Dependency consumer key must not be empty.");
        }

        // BDL024/BDL025: Update feed validation
        if (model.UpdateFeed is not null)
        {
            if (!Uri.TryCreate(model.UpdateFeed.FeedUrl, UriKind.Absolute, out var feedUri))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL024: Update feed URL '{model.UpdateFeed.FeedUrl}' is not a valid absolute URI.");
            else if (feedUri.Scheme != Uri.UriSchemeHttps)
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL025: Update feed URL must use HTTPS scheme, got '{feedUri.Scheme}'.");
        }

        return Unit.Value;
    }
}
