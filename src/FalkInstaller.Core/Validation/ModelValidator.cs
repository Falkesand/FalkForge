namespace FalkInstaller.Validation;

using FalkInstaller.Models;

public static class ModelValidator
{
    public static ValidationResult Validate(PackageModel package)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(package.Name))
            result.AddError("PKG001", "Package Name is required.");

        if (string.IsNullOrWhiteSpace(package.Manufacturer))
            result.AddError("PKG002", "Package Manufacturer is required.");

        if (package.Version is null)
            result.AddError("PKG003", "Package Version is required.");
        else if (package.Version.Major == 0 && package.Version.Minor == 0 && package.Version.Build == 0)
            result.AddWarning("PKG004", "Package Version is 0.0.0.");

        if (package.Version is not null && package.Version.Major > 255)
            result.AddError("PKG005", "MSI major version cannot exceed 255.");

        if (package.Version is not null && package.Version.Minor > 255)
            result.AddError("PKG006", "MSI minor version cannot exceed 255.");

        if (package.Version is not null && package.Version.Build > 65535)
            result.AddError("PKG007", "MSI build version cannot exceed 65535.");

        if (package.Name.Length > 64)
            result.AddWarning("PKG008", "Package Name exceeds 64 characters, which may cause display issues.");

        if (package.UpgradeCode == Guid.Empty)
            result.AddError("PKG009", "UpgradeCode must not be empty GUID.");

        if (package.ProductCode == Guid.Empty)
            result.AddError("PKG010", "ProductCode must not be empty GUID.");

        if (package.Files.Count == 0 && package.Features.All(f => f.ComponentRefs.Count == 0))
            result.AddWarning("PKG011", "Package has no files. The MSI will be empty.");

        ValidateFeatures(package.Features, result);
        ValidateServices(package.Services, result);
        ValidateShortcuts(package.Shortcuts, result);

        return result;
    }

    private static void ValidateFeatures(IReadOnlyList<FeatureModel> features, ValidationResult result)
    {
        var ids = new HashSet<string>();
        foreach (var feature in features)
        {
            ValidateFeature(feature, ids, result);
        }
    }

    private static void ValidateFeature(FeatureModel feature, HashSet<string> ids, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(feature.Id))
            result.AddError("FEA001", "Feature Id is required.");
        else if (!ids.Add(feature.Id))
            result.AddError("FEA002", $"Duplicate feature Id: '{feature.Id}'.");

        if (string.IsNullOrWhiteSpace(feature.Title))
            result.AddError("FEA003", $"Feature '{feature.Id}' must have a Title.");

        foreach (var child in feature.Children)
        {
            ValidateFeature(child, ids, result);
        }
    }

    private static void ValidateServices(IReadOnlyList<ServiceModel> services, ValidationResult result)
    {
        foreach (var service in services)
        {
            if (string.IsNullOrWhiteSpace(service.Name))
                result.AddError("SVC001", "Service Name is required.");

            if (string.IsNullOrWhiteSpace(service.Executable))
                result.AddError("SVC002", $"Service '{service.Name}' must have an Executable.");

            if (service.Account == ServiceAccount.User && string.IsNullOrWhiteSpace(service.UserName))
                result.AddError("SVC003", $"Service '{service.Name}' uses User account but no UserName specified.");

            if (service.Name.Length > 256)
                result.AddError("SVC004", $"Service name '{service.Name}' exceeds 256 characters.");
        }
    }

    private static void ValidateShortcuts(IReadOnlyList<ShortcutModel> shortcuts, ValidationResult result)
    {
        foreach (var shortcut in shortcuts)
        {
            if (string.IsNullOrWhiteSpace(shortcut.Name))
                result.AddError("SHC001", "Shortcut Name is required.");

            if (string.IsNullOrWhiteSpace(shortcut.TargetFile))
                result.AddError("SHC002", $"Shortcut '{shortcut.Name}' must have a TargetFile.");

            if (shortcut.Locations.Count == 0)
                result.AddWarning("SHC003", $"Shortcut '{shortcut.Name}' has no locations specified.");
        }
    }
}
