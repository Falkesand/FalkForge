using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for PackageRules (PKG001-011).
/// Each test calls a rule directly via RuleContext.ForTest — no full orchestrator.
/// </summary>
public sealed class PackageRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel Base(string name = "App", string manufacturer = "Corp") => new()
    {
        Name = name,
        Manufacturer = manufacturer,
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid()
    };

    // ── PKG001 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg001_blank_name_yields_error()
    {
        var violations = PackageRules.Pkg001_NameRequired.Evaluate(Ctx(Base(name: ""))).ToList();

        Assert.Single(violations);
        Assert.Equal("PKG001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Pkg001_whitespace_name_yields_error()
    {
        var violations = PackageRules.Pkg001_NameRequired.Evaluate(Ctx(Base(name: "  "))).ToList();
        Assert.Single(violations);
    }

    [Fact]
    public void Pkg001_valid_name_yields_no_violations()
    {
        Assert.Empty(PackageRules.Pkg001_NameRequired.Evaluate(Ctx(Base())));
    }

    // ── PKG002 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg002_blank_manufacturer_yields_error()
    {
        var violations = PackageRules.Pkg002_ManufacturerRequired.Evaluate(Ctx(Base(manufacturer: ""))).ToList();

        Assert.Single(violations);
        Assert.Equal("PKG002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Pkg002_valid_manufacturer_yields_no_violations()
    {
        Assert.Empty(PackageRules.Pkg002_ManufacturerRequired.Evaluate(Ctx(Base())));
    }

    // ── PKG003 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg003_null_version_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = null!,
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg003_VersionRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PKG003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Pkg003_valid_version_yields_no_violations()
    {
        Assert.Empty(PackageRules.Pkg003_VersionRequired.Evaluate(Ctx(Base())));
    }

    // ── PKG004 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg004_version_000_yields_warning()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(0, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg004_VersionZeroWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PKG004", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Pkg004_non_zero_version_yields_no_violations()
    {
        Assert.Empty(PackageRules.Pkg004_VersionZeroWarning.Evaluate(Ctx(Base())));
    }

    // ── PKG005 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg005_major_above_255_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(256, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg005_MajorVersionLimit.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG005", violations[0].RuleId.Value);
    }

    [Fact]
    public void Pkg005_major_255_is_valid()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(255, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };
        Assert.Empty(PackageRules.Pkg005_MajorVersionLimit.Evaluate(Ctx(pkg)));
    }

    // ── PKG006 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg006_minor_above_255_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 256, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg006_MinorVersionLimit.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG006", violations[0].RuleId.Value);
    }

    // ── PKG007 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg007_build_above_65535_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 65536),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg007_BuildVersionLimit.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG007", violations[0].RuleId.Value);
    }

    // ── PKG008 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg008_name_over_64_chars_yields_warning()
    {
        var longName = new string('X', 65);
        var pkg = Base(name: longName);

        var violations = PackageRules.Pkg008_NameLengthWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PKG008", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Pkg008_name_exactly_64_chars_is_valid()
    {
        var exactName = new string('X', 64);
        Assert.Empty(PackageRules.Pkg008_NameLengthWarning.Evaluate(Ctx(Base(name: exactName))));
    }

    // ── PKG009 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg009_empty_upgrade_code_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.Empty, ProductCode = Guid.NewGuid()
        };

        var violations = PackageRules.Pkg009_UpgradeCodeRequired.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG009", violations[0].RuleId.Value);
    }

    [Fact]
    public void Pkg009_valid_upgrade_code_is_fine()
    {
        Assert.Empty(PackageRules.Pkg009_UpgradeCodeRequired.Evaluate(Ctx(Base())));
    }

    // ── PKG010 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg010_empty_product_code_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.Empty
        };

        var violations = PackageRules.Pkg010_ProductCodeRequired.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG010", violations[0].RuleId.Value);
    }

    // ── PKG011 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Pkg011_no_files_and_no_component_refs_yields_warning()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Features = [new FeatureModel { Id = "F1", Title = "F1", ComponentRefs = [] }]
        };

        var violations = PackageRules.Pkg011_EmptyPackageWarning.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
        Assert.Equal("PKG011", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Pkg011_package_with_files_is_fine()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "app.exe",
                    TargetDirectory = KnownFolder.ProgramFiles / "App",
                    FileName = "app.exe"
                }
            ]
        };

        Assert.Empty(PackageRules.Pkg011_EmptyPackageWarning.Evaluate(Ctx(pkg)));
    }
}
