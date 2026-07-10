namespace FalkForge.Extensions.Driver;

public static class DriverValidator
{
    public static IReadOnlyList<DriverValidationError> Validate(IReadOnlyList<DriverModel> drivers)
    {
        var errors = new List<DriverValidationError>();

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var driver in drivers)
        {
            if (!string.IsNullOrWhiteSpace(driver.Id) && !seenIds.Add(driver.Id))
                errors.Add(new DriverValidationError("DRV004", $"Duplicate driver Id '{driver.Id}'."));

            ValidateDriver(driver, errors);
        }

        return errors;
    }

    private static void ValidateDriver(DriverModel driver, List<DriverValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(driver.Id))
            errors.Add(new DriverValidationError("DRV001", "Driver Id must not be empty."));

        if (string.IsNullOrWhiteSpace(driver.InfFilePath))
            errors.Add(new DriverValidationError("DRV002", $"Driver '{driver.Id}' must have an InfFilePath."));

        if (!string.IsNullOrWhiteSpace(driver.InfFilePath) &&
            !driver.InfFilePath.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
            errors.Add(new DriverValidationError("DRV003", $"Driver '{driver.Id}' InfFilePath must end with '.inf'."));

        // InfFilePath is embedded in a pnputil command line executed as SYSTEM by a
        // deferred custom action; an embedded quote or newline would break out of the
        // quoting and inject extra arguments. Mirrors the HttpValidator netsh guard.
        if (!string.IsNullOrWhiteSpace(driver.InfFilePath) &&
            driver.InfFilePath.AsSpan().IndexOfAny('"', '\r', '\n') >= 0)
            errors.Add(new DriverValidationError("DRV005", $"Driver '{driver.Id}' InfFilePath contains characters not permitted in pnputil commands."));
    }
}
