using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Proves feature-gating for <c>PermissionModel</c>. Unlike Shortcut/Environment/IniFile/
/// FileAssociation, the real MSI <c>LockPermissions</c> and <c>MsiLockPermissionsEx</c> tables
/// carry no <c>Component_</c> or <c>Feature_</c> column — a permission row is not itself an
/// installable unit, it is always applied to whatever object (File/Registry/CreateFolder) its
/// <c>LockObject</c> names. Synthesizing a carrier component for a permission row (as done for
/// Service/Registry) would therefore be a "FeatureRef that sets state but doesn't change the
/// compiled MSI" — the row has nowhere to attach it.
///
/// <c>MsiLockPermissionsEx</c> (the SDDL-driven half) does carry a <c>Condition</c> column,
/// which is the officially-supported MSI mechanism for making a permission row's execution
/// conditional on a feature's install state (the <c>&amp;FeatureId=3</c> feature-state condition
/// syntax — "feature installed locally"). This is the real, structurally-correct gating
/// mechanism for SDDL permissions and is what this test proves.
///
/// The User/Domain-driven half (<c>LockPermissions</c>) has no Condition column at all, so it
/// cannot be gated by any mechanism; <see cref="FalkForge.Validation.MiscRules.Prm005_FeatureRefRequiresSddl"/>
/// fails the compile loudly instead of silently dropping the author's FeatureRef.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FeatureGatedPermissionConditionTests
{
    [Fact]
    public void Compile_SddlPermissionGatedToFeature_MsiLockPermissionsExConditionEncodesFeature()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatPerm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for permission feature-gating test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FeatPermApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FeatPermApp"));
                p.Feature("FeatureA", _ => { });
                p.Feature("FeatureB", f =>
                {
                    f.Permission("GatedUnrelatedLock", perm =>
                    {
                        perm.Sddl = "D:(A;;FA;;;BA)";
                        perm.ForTable("CreateFolder");
                    });
                });
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows(
                "SELECT `LockObject`, `Condition` FROM `MsiLockPermissionsEx` WHERE `LockObject` = 'GatedUnrelatedLock'", 2).Value;
            var row = Assert.Single(rows);
            Assert.Equal("&FeatureB=3", row[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_UngatedSddlPermission_MsiLockPermissionsExConditionIsNull()
    {
        // WHY: the fallback path (no FeatureRef) must stay byte-identical to today's compiled
        // output — Condition stays null, exactly as before this change.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFeatPermUngated_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for ungated permission test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "UngatedPermApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "UngatedPermApp"));
                p.Permission("UngatedLock", perm =>
                {
                    perm.Sddl = "D:(A;;FA;;;BA)";
                    perm.ForTable("CreateFolder");
                });
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows(
                "SELECT `LockObject`, `Condition` FROM `MsiLockPermissionsEx` WHERE `LockObject` = 'UngatedLock'", 2).Value;
            var row = Assert.Single(rows);
            Assert.Null(row[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Validate_UserPermissionWithFeatureRef_ProducesPrm005()
    {
        // WHY: LockPermissions (the User/Domain-driven table) has no Condition or Component
        // column at all in real MSI — a FeatureRef on a non-SDDL permission has no honest
        // compiled representation. Fail loud rather than silently accepting and ignoring it.
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Acme",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Permissions = [new PermissionModel
            {
                LockObject = "SomeLock",
                Table = "CreateFolder",
                User = "Users",
                FeatureRef = "FeatureB"
            }],
            Features = [new FeatureModel
            {
                Id = "FeatureB", Title = "FeatureB", IsRequired = true, IsDefault = true
            }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "PRM005");
    }
}
