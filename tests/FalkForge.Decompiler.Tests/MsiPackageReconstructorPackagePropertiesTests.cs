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

    // ── IsAdmin round-trip (AdminProperties) ─────────────────────────────────

    [Fact]
    public void Rebuild_PropertyListedInAdminProperties_IsAdmin()
    {
        var properties = Reconstruct(
            new PropertyRow("DEPLOY_TIER", "prod"),
            new PropertyRow("AdminProperties", "DEPLOY_TIER"));

        var property = Assert.Single(properties, p => p.Name == "DEPLOY_TIER");
        Assert.True(property.IsAdmin);
    }

    [Fact]
    public void Rebuild_PropertyNotListedInAdminProperties_IsNotAdmin()
    {
        var properties = Reconstruct(
            new PropertyRow("DEPLOY_TIER", "prod"),
            new PropertyRow("AdminProperties", "OTHER_PROP"));

        var property = Assert.Single(properties, p => p.Name == "DEPLOY_TIER");
        Assert.False(property.IsAdmin);
    }

    [Fact]
    public void Rebuild_AdminPropertiesItself_DoesNotLeakBackAsUserProperty()
    {
        // AdminProperties is a compiler-computed, internal MSI property (like
        // SecureCustomProperties/MsiHiddenProperties) — it must not reappear as an ordinary
        // reconstructed PropertyModel, or a round-tripped package would re-author it as a
        // regular property (PRP002 would then reject it on the next compile).
        var properties = Reconstruct(
            new PropertyRow("DEPLOY_TIER", "prod"),
            new PropertyRow("AdminProperties", "DEPLOY_TIER"));

        Assert.DoesNotContain(properties, p => p.Name == "AdminProperties");
    }

    // ── IsHidden round-trip (MsiHiddenProperties) ────────────────────────────

    [Fact]
    public void Rebuild_PropertyListedInMsiHiddenProperties_IsHidden()
    {
        var properties = Reconstruct(
            new PropertyRow("APP_SECRET", "value"),
            new PropertyRow("MsiHiddenProperties", "APP_SECRET"));

        var property = Assert.Single(properties, p => p.Name == "APP_SECRET");
        Assert.True(property.IsHidden);
    }

    [Fact]
    public void Rebuild_PropertyNotListedInMsiHiddenProperties_IsNotHidden()
    {
        var properties = Reconstruct(
            new PropertyRow("APP_SECRET", "value"),
            new PropertyRow("MsiHiddenProperties", "OTHER_PROP"));

        var property = Assert.Single(properties, p => p.Name == "APP_SECRET");
        Assert.False(property.IsHidden);
    }

    [Fact]
    public void Rebuild_HiddenNameWithNoMatchingPropertyRow_DoesNotFabricateAPropertyModel()
    {
        // MsiHiddenProperties can legitimately list a name that is NOT itself a Property-table row —
        // e.g. an extension's deferred-action CustomActionData carrier property (set only at run time
        // via SetProperty, never authored as a Property row). Reconstruction must not invent a
        // PropertyModel for a name that has no backing row.
        var properties = Reconstruct(
            new PropertyRow("APP_SECRET", "value"),
            new PropertyRow("MsiHiddenProperties", "APP_SECRET;SqlDb_AppDb"));

        Assert.DoesNotContain(properties, p => p.Name == "SqlDb_AppDb");
        Assert.Single(properties, p => p.Name == "APP_SECRET");
    }
}
