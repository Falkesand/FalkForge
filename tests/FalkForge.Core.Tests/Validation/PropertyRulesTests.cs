using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for PropertyRules (PRP001) — the authoring-time guard that catches a
/// secure property whose name can never legally appear in SecureCustomProperties (Windows
/// Installer public property names cannot contain lowercase letters).
/// </summary>
public sealed class PropertyRulesTests
{
    private static RuleContext Ctx(params PropertyModel[] properties) => RuleContext.ForTest(Base(properties));

    private static PackageModel Base(params PropertyModel[] properties) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Properties = properties
    };

    private static PropertyModel Prop(string name, string value = "x", bool secure = false, bool hidden = false, bool admin = false)
        => new() { Name = name, Value = value, IsSecure = secure, IsHidden = hidden, IsAdmin = admin };

    // ── PRP001 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Prp001_lowercase_name_marked_secure_yields_error()
    {
        var violations = PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("dbPassword", secure: true))).ToList();

        Assert.Single(violations);
        Assert.Equal("PRP001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Prp001_all_uppercase_name_marked_secure_is_valid()
    {
        Assert.Empty(PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("DBPASSWORD", secure: true))));
    }

    [Fact]
    public void Prp001_lowercase_name_marked_hidden_only_is_valid()
    {
        // IsHidden has no casing rule in MSI (MsiHiddenProperties). Only IsSecure is uppercase-gated.
        Assert.Empty(PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("dbPassword", hidden: true))));
    }

    [Fact]
    public void Prp001_lowercase_name_marked_admin_only_is_valid()
    {
        // IsAdmin (AdminProperties) explicitly allows mixed-case names in MSI. Only IsSecure is uppercase-gated.
        Assert.Empty(PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("dbPassword", admin: true))));
    }

    [Fact]
    public void Prp001_uppercase_name_with_digits_and_underscores_marked_secure_is_valid()
    {
        Assert.Empty(PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("DB_PASSWORD_2", secure: true))));
    }

    [Fact]
    public void Prp001_unflagged_lowercase_name_is_valid()
    {
        Assert.Empty(PropertyRules.Prp001_SecurePropertyMustBeUppercase
            .Evaluate(Ctx(Prop("dbPassword"))));
    }
}
