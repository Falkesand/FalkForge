using FalkInstaller.Models;
using FalkInstaller.Validation;

namespace FalkInstaller.Testing;

public static class InstallerValidator
{
    public static ValidationResult Validate(PackageModel package)
    {
        return ModelValidator.Validate(package);
    }

    public static ValidationResult ValidateAndAssertValid(PackageModel package)
    {
        var result = ModelValidator.Validate(package);
        if (!result.IsValid)
        {
            var errors = string.Join(Environment.NewLine, result.Errors.Select(e => $"  {e.Code}: {e.Message}"));
            throw new InvalidOperationException($"Package validation failed:{Environment.NewLine}{errors}");
        }
        return result;
    }
}
