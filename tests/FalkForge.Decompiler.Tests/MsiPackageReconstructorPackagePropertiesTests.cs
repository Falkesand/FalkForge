using FalkForge.Decompiler.Recipe;
using FalkForge.Decompiler.Recipe.Schemas;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// Regression coverage for user-property reconstruction (<see cref="MsiPackageReconstructor.Rebuild"/>).
/// An all-uppercase MSI property name makes a property <em>public</em> (overridable from the command
/// line) — it does not make it <em>secure</em>. Only names listed in the <c>SecureCustomProperties</c>
/// property are secure (passed through to the elevated execute sequence). Reconstructing
/// <c>PropertyModel.IsSecure</c> from uppercase naming alone marks every public-only property secure,
/// which is wrong per <see href="https://learn.microsoft.com/windows/win32/msi/securecustomproperties"/>.
/// </summary>
public sealed class MsiPackageReconstructorPackagePropertiesTests
{
    private static IReadOnlyList<FalkForge.Models.PropertyModel> Reconstruct(params PropertyRow[] propertyRows)
        => MsiPackageReconstructor.Rebuild(
            propertyRows: propertyRows,
            directoryRows: [],
            componentRows: [],
            fileRows: [],
            featureRows: [],
            featureComponentsRows: [],
            registryRows: [],
            serviceRows: [],
            shortcutRows: [],
            upgradeRows: []).Properties;

    [Fact]
    public void Rebuild_UppercasePropertyNotListedInSecureCustomProperties_IsNotSecure()
    {
        var properties = Reconstruct(
            new PropertyRow("PUBLIC_NOT_SECURE", "value"),
            new PropertyRow("SecureCustomProperties", "OTHER_PROP"));

        var property = Assert.Single(properties, p => p.Name == "PUBLIC_NOT_SECURE");
        Assert.False(property.IsSecure);
    }

    [Fact]
    public void Rebuild_PropertyListedInSecureCustomProperties_IsSecure()
    {
        var properties = Reconstruct(
            new PropertyRow("PRODUCT_PASSWORD", "secret"),
            new PropertyRow("SecureCustomProperties", "PRODUCT_PASSWORD"));

        var property = Assert.Single(properties, p => p.Name == "PRODUCT_PASSWORD");
        Assert.True(property.IsSecure);
    }
}
