using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class SensitivePropertyValidationTests
{
    [Fact]
    public void Registry_WithPasswordProperty_EmitsWarning()
    {
        var package = CreatePackageWithRegistryValue("[SERVICEPASSWORD]");

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "REG007");
    }

    [Fact]
    public void Registry_WithSecretProperty_EmitsWarning()
    {
        var package = CreatePackageWithRegistryValue("[API_SECRET]");

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "REG007");
    }

    [Fact]
    public void Registry_WithNormalProperty_NoWarning()
    {
        var package = CreatePackageWithRegistryValue("[INSTALLFOLDER]");

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.RuleId.Value == "REG007");
    }

    [Fact]
    public void Registry_WithNoPropertyReference_NoWarning()
    {
        var package = CreatePackageWithRegistryValue("just a plain string");

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.RuleId.Value == "REG007");
    }

    [Fact]
    public void Registry_WithMixedContent_EmitsWarning()
    {
        var package = CreatePackageWithRegistryValue("Server=[HOST];Password=[DB_PASSWORD]");

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "REG007");
    }

    private static PackageModel CreatePackageWithRegistryValue(object? value)
    {
        return new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            RegistryEntries =
            [
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = @"SOFTWARE\TestApp",
                    ValueName = "ConnectionString",
                    Value = value
                }
            ],
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main Feature"
                }
            ]
        };
    }
}
