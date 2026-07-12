using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for remaining rule groups:
/// Custom actions CA001-005, Assemblies ASM001-003,
/// MediaTemplate MDT001-004, Signing SGN001-003,
/// MajorUpgrade MUP001/MUP003, Downgrade DNG001-002.
/// </summary>
public sealed class RemainingRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel Base() => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid()
    };

    private static PackageModel WithCA(params CustomActionModel[] cas) => new()
    {
        Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
        CustomActions = cas.ToList()
    };

    private static PackageModel WithAssemblies(params AssemblyModel[] asms) => new()
    {
        Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
        Assemblies = asms.ToList()
    };

    // ── CA001 — Custom action Id required ────────────────────────────────────

    [Fact]
    public void Ca001_empty_id_yields_error()
    {
        var pkg = WithCA(new CustomActionModel { Id = "", Type = 1, SourceRef = "SomeRef" });
        var violations = RemainingRules.Ca001_IdRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Ca001_valid_id_yields_no_violations()
    {
        var pkg = WithCA(new CustomActionModel { Id = "MyCA", Type = 1, SourceRef = "Ref" });
        Assert.Empty(RemainingRules.Ca001_IdRequired.Evaluate(Ctx(pkg)));
    }

    // ── CA002 — Custom action Type required ──────────────────────────────────

    [Fact]
    public void Ca002_zero_type_yields_error()
    {
        var pkg = WithCA(new CustomActionModel { Id = "CA1", Type = 0, SourceRef = "Ref" });
        var violations = RemainingRules.Ca002_TypeRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA002", violations[0].RuleId.Value);
    }

    // ── CA003 — Custom action SourceRef required ──────────────────────────────

    [Fact]
    public void Ca003_empty_source_ref_yields_error()
    {
        var pkg = WithCA(new CustomActionModel { Id = "CA1", Type = 1, SourceRef = "" });
        var violations = RemainingRules.Ca003_SourceRefRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA003", violations[0].RuleId.Value);
    }

    // ── CA004 — Rollback and Commit mutually exclusive ────────────────────────

    [Fact]
    public void Ca004_rollback_and_commit_together_yields_error()
    {
        var type = CustomActionType.Rollback | CustomActionType.Commit;
        var pkg = WithCA(new CustomActionModel { Id = "CA1", Type = type, SourceRef = "Ref" });
        var violations = RemainingRules.Ca004_RollbackCommitExclusive.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA004", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ca004_rollback_only_yields_no_violations()
    {
        var pkg = WithCA(new CustomActionModel
        {
            Id = "CA1",
            Type = CustomActionType.Rollback | CustomActionType.InScript,
            SourceRef = "Ref"
        });
        Assert.Empty(RemainingRules.Ca004_RollbackCommitExclusive.Evaluate(Ctx(pkg)));
    }

    // ── CA005 — NoImpersonate without InScript warning ────────────────────────

    [Fact]
    public void Ca005_no_impersonate_without_in_script_yields_warning()
    {
        var pkg = WithCA(new CustomActionModel
        {
            Id = "CA1",
            Type = CustomActionType.NoImpersonate, // no InScript
            SourceRef = "Ref"
        });
        var violations = RemainingRules.Ca005_NoImpersonateRequiresInScript.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA005", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Ca005_no_impersonate_with_in_script_yields_no_violations()
    {
        var pkg = WithCA(new CustomActionModel
        {
            Id = "CA1",
            Type = CustomActionType.NoImpersonate | CustomActionType.InScript,
            SourceRef = "Ref"
        });
        Assert.Empty(RemainingRules.Ca005_NoImpersonateRequiresInScript.Evaluate(Ctx(pkg)));
    }

    // ── CA006 — Custom action defined but never scheduled (warning) ──────────

    [Fact]
    public void Ca006_defined_but_never_scheduled_yields_warning()
    {
        var pkg = WithCA(new CustomActionModel { Id = "OrphanCA", Type = 1, SourceRef = "Ref" });
        var violations = RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA006", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Ca006_scheduled_in_execute_sequence_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            CustomActions = [new CustomActionModel { Id = "ScheduledCA", Type = 1, SourceRef = "Ref" }],
            ExecuteSequenceActions =
            [
                new SequenceActionModel
                {
                    ActionName = "ScheduledCA",
                    Table = SequenceTable.InstallExecuteSequence,
                    Position = new ActionPosition.AfterAction("InstallFiles")
                }
            ]
        };
        Assert.Empty(RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Ca006_scheduled_in_ui_sequence_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            CustomActions = [new CustomActionModel { Id = "ScheduledCA", Type = 1, SourceRef = "Ref" }],
            UISequenceActions =
            [
                new SequenceActionModel
                {
                    ActionName = "ScheduledCA",
                    Table = SequenceTable.InstallUISequence,
                    Position = new ActionPosition.AtNumber(1500)
                }
            ]
        };
        Assert.Empty(RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Ca006_no_custom_actions_yields_no_violations()
    {
        Assert.Empty(RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(Base())));
    }

    [Fact]
    public void Ca006_one_of_two_actions_unscheduled_flags_only_that_one()
    {
        // Guards against a per-CA check that over-reports (flags the scheduled action too)
        // or under-reports (misses the orphan) once more than one custom action is in play.
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            CustomActions =
            [
                new CustomActionModel { Id = "ScheduledCA", Type = 1, SourceRef = "Ref" },
                new CustomActionModel { Id = "OrphanCA", Type = 1, SourceRef = "Ref" }
            ],
            ExecuteSequenceActions =
            [
                new SequenceActionModel
                {
                    ActionName = "ScheduledCA",
                    Table = SequenceTable.InstallExecuteSequence,
                    Position = new ActionPosition.AfterAction("InstallFiles")
                }
            ]
        };
        var violations = RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(pkg)).ToList();

        var violation = Assert.Single(violations);
        Assert.Contains("OrphanCA", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ca006_case_mismatched_schedule_reference_still_yields_warning()
    {
        // MSI action identifiers are case-sensitive (the compiler's own reference resolution
        // uses StringComparison.Ordinal), so a schedule entry that differs only in case from the
        // CustomAction's Id does not actually schedule it. Pins the deliberate Ordinal choice in
        // Ca006_DefinedButNeverScheduled against a well-meaning switch to OrdinalIgnoreCase.
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            CustomActions = [new CustomActionModel { Id = "OrphanCA", Type = 1, SourceRef = "Ref" }],
            ExecuteSequenceActions =
            [
                new SequenceActionModel
                {
                    ActionName = "orphanca", // case mismatch — does not schedule "OrphanCA"
                    Table = SequenceTable.InstallExecuteSequence,
                    Position = new ActionPosition.AfterAction("InstallFiles")
                }
            ]
        };
        var violations = RemainingRules.Ca006_DefinedButNeverScheduled.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CA006", violations[0].RuleId.Value);
    }

    // ── ASM001 — Assembly FileRef required ───────────────────────────────────

    [Fact]
    public void Asm001_empty_file_ref_yields_error()
    {
        var pkg = WithAssemblies(new AssemblyModel { FileRef = "" });
        var violations = RemainingRules.Asm001_FileRefRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("ASM001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Asm001_valid_file_ref_yields_no_violations()
    {
        var pkg = WithAssemblies(new AssemblyModel { FileRef = "MyApp.exe" });
        Assert.Empty(RemainingRules.Asm001_FileRefRequired.Evaluate(Ctx(pkg)));
    }

    // ── ASM002 — GAC assembly should have PublicKeyToken ────────────────────

    [Fact]
    public void Asm002_gac_without_public_key_token_yields_warning()
    {
        var pkg = WithAssemblies(new AssemblyModel
        {
            FileRef = "Lib.dll",
            Type = AssemblyType.DotNetAssembly,
            ApplicationFileRef = null, // GAC = no AppFileRef
            AssemblyPublicKeyToken = null
        });
        var violations = RemainingRules.Asm002_GacPublicKeyTokenWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("ASM002", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Asm002_gac_with_public_key_token_yields_no_violations()
    {
        var pkg = WithAssemblies(new AssemblyModel
        {
            FileRef = "Lib.dll",
            ApplicationFileRef = null,
            AssemblyPublicKeyToken = "31BF3856AD364E35"
        });
        Assert.Empty(RemainingRules.Asm002_GacPublicKeyTokenWarning.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Asm002_app_private_assembly_without_token_yields_no_violations()
    {
        var pkg = WithAssemblies(new AssemblyModel
        {
            FileRef = "Lib.dll",
            ApplicationFileRef = "App.exe" // private, not GAC
        });
        Assert.Empty(RemainingRules.Asm002_GacPublicKeyTokenWarning.Evaluate(Ctx(pkg)));
    }

    // ── ASM003 — Assembly version format ────────────────────────────────────

    [Fact]
    public void Asm003_invalid_version_format_yields_error()
    {
        var pkg = WithAssemblies(new AssemblyModel
        {
            FileRef = "Lib.dll",
            AssemblyVersion = "1.0"
        });
        var violations = RemainingRules.Asm003_VersionFormat.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("ASM003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Asm003_valid_four_part_version_yields_no_violations()
    {
        var pkg = WithAssemblies(new AssemblyModel
        {
            FileRef = "Lib.dll",
            AssemblyVersion = "1.2.3.4"
        });
        Assert.Empty(RemainingRules.Asm003_VersionFormat.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Asm003_null_version_yields_no_violations()
    {
        var pkg = WithAssemblies(new AssemblyModel { FileRef = "Lib.dll" });
        Assert.Empty(RemainingRules.Asm003_VersionFormat.Evaluate(Ctx(pkg)));
    }

    // ── MDT001 — MediaTemplate CabinetTemplate required ──────────────────────

    [Fact]
    public void Mdt001_empty_cabinet_template_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel { CabinetTemplate = "" }
        };
        var violations = RemainingRules.Mdt001_CabinetTemplateRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MDT001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Mdt001_no_media_template_yields_no_violations()
    {
        Assert.Empty(RemainingRules.Mdt001_CabinetTemplateRequired.Evaluate(Ctx(Base())));
    }

    // ── MDT002 — CabinetTemplate must contain {0} ────────────────────────────

    [Fact]
    public void Mdt002_cabinet_template_without_placeholder_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel { CabinetTemplate = "cab.cab" }
        };
        var violations = RemainingRules.Mdt002_CabinetTemplatePlaceholder.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MDT002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Mdt002_cabinet_template_with_placeholder_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel { CabinetTemplate = "cab{0}.cab" }
        };
        Assert.Empty(RemainingRules.Mdt002_CabinetTemplatePlaceholder.Evaluate(Ctx(pkg)));
    }

    // ── MDT003 — MaximumCabinetSizeInMB non-negative ─────────────────────────

    [Fact]
    public void Mdt003_negative_cabinet_size_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel { CabinetTemplate = "cab{0}.cab", MaximumCabinetSizeInMB = -1 }
        };
        var violations = RemainingRules.Mdt003_CabinetSizeNonNegative.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MDT003", violations[0].RuleId.Value);
    }

    // ── MDT004 — MaximumUncompressedMediaSize non-negative ────────────────────

    [Fact]
    public void Mdt004_negative_uncompressed_size_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel { CabinetTemplate = "cab{0}.cab", MaximumUncompressedMediaSize = -1 }
        };
        var violations = RemainingRules.Mdt004_UncompressedSizeNonNegative.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MDT004", violations[0].RuleId.Value);
    }

    // ── SGN001 — PFX certificate warning ─────────────────────────────────────

    [Fact]
    public void Sgn001_pfx_certificate_yields_warning()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions { CertificatePath = "cert.pfx" }
        };
        var violations = RemainingRules.Sgn001_PfxCertificateWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SGN001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Sgn001_cer_certificate_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions { CertificatePath = "cert.cer" }
        };
        Assert.Empty(RemainingRules.Sgn001_PfxCertificateWarning.Evaluate(Ctx(pkg)));
    }

    // ── SGN002 — Signing requires CertificatePath or CertificateThumbprint ───

    [Fact]
    public void Sgn002_signing_with_no_cert_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions()
        };
        var violations = RemainingRules.Sgn002_CertificateRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SGN002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Sgn002_no_signing_yields_no_violations()
    {
        Assert.Empty(RemainingRules.Sgn002_CertificateRequired.Evaluate(Ctx(Base())));
    }

    // ── SGN003 — DigestAlgorithm must be sha256/sha384/sha512 ─────────────────

    [Fact]
    public void Sgn003_invalid_digest_algorithm_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions { CertificateThumbprint = "ABCD", DigestAlgorithm = "md5" }
        };
        var violations = RemainingRules.Sgn003_DigestAlgorithmValid.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SGN003", violations[0].RuleId.Value);
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("sha384")]
    [InlineData("sha512")]
    public void Sgn003_valid_digest_algorithms_yield_no_violations(string algo)
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            Signing = new SigningOptions { CertificateThumbprint = "ABCD", DigestAlgorithm = algo }
        };
        Assert.Empty(RemainingRules.Sgn003_DigestAlgorithmValid.Evaluate(Ctx(pkg)));
    }

    // ── MUP001 — MajorUpgrade requires UpgradeCode ───────────────────────────

    [Fact]
    public void Mup001_major_upgrade_with_empty_upgrade_code_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.Empty, // empty
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel()
        };
        var violations = RemainingRules.Mup001_UpgradeCodeRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MUP001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Mup001_no_major_upgrade_yields_no_violations()
    {
        Assert.Empty(RemainingRules.Mup001_UpgradeCodeRequired.Evaluate(Ctx(Base())));
    }

    // ── MUP003 — MajorUpgrade and Upgrade cannot coexist ─────────────────────

    [Fact]
    public void Mup003_major_upgrade_and_upgrade_table_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Upgrade = new UpgradeModel { MinimumVersion = "1.0.0", MaximumVersion = "2.0.0" }
        };
        var violations = RemainingRules.Mup003_NoConflictWithUpgradeTable.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MUP003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Mup003_only_major_upgrade_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel()
        };
        Assert.Empty(RemainingRules.Mup003_NoConflictWithUpgradeTable.Evaluate(Ctx(pkg)));
    }

    // ── DNG001 — Downgrade block requires error message ───────────────────────

    [Fact]
    public void Dng001_block_downgrade_without_message_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = false, ErrorMessage = null }
        };
        var violations = RemainingRules.Dng001_BlockRequiresMessage.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("DNG001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Dng001_allow_downgrades_yields_no_violations()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = true }
        };
        Assert.Empty(RemainingRules.Dng001_BlockRequiresMessage.Evaluate(Ctx(pkg)));
    }

    // ── DNG002 — Downgrade requires MajorUpgrade ─────────────────────────────

    [Fact]
    public void Dng002_downgrade_without_major_upgrade_yields_error()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid(),
            MajorUpgrade = null,
            Downgrade = new DowngradeModel { AllowDowngrades = true }
        };
        var violations = RemainingRules.Dng002_RequiresMajorUpgrade.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("DNG002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Dng002_no_downgrade_config_yields_no_violations()
    {
        Assert.Empty(RemainingRules.Dng002_RequiresMajorUpgrade.Evaluate(Ctx(Base())));
    }
}
