using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class PropertyTableProducerTests
{
    [Fact]
    public void Schema_has_correct_name()
    {
        PropertyTableProducer producer = new();

        Assert.Equal("Property", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_two_columns_with_property_pk()
    {
        PropertyTableProducer producer = new();

        Assert.Equal(2, producer.Schema.Columns.Length);
        Assert.Equal("Property", producer.Schema.Columns[0].Name);
        Assert.Equal("Value", producer.Schema.Columns[1].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.True(producer.Schema.ForeignKeys.IsEmpty);
    }

    [Fact]
    public void Produce_emits_synthesized_builtins_for_default_package()
    {
        System.Guid productCode = System.Guid.Parse("11111111-2222-3333-4444-555555555555");
        System.Guid upgradeCode = System.Guid.Parse("66666666-7777-8888-9999-AAAAAAAAAAAA");

        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            name: "MyApp",
            manufacturer: "Acme",
            version: new System.Version(2, 1, 0),
            productCode: productCode,
            upgradeCode: upgradeCode,
            scope: InstallScope.PerMachine);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "ProductName", "MyApp");
        AssertRow(rows, "Manufacturer", "Acme");
        AssertRow(rows, "ProductVersion", "2.1.0");
        AssertRow(rows, "ProductCode", productCode.ToString("B").ToUpperInvariant());
        AssertRow(rows, "UpgradeCode", upgradeCode.ToString("B").ToUpperInvariant());
        AssertRow(rows, "ProductLanguage", "1033");
        AssertRow(rows, "ALLUSERS", "1");
    }

    [Fact]
    public void Produce_with_per_user_package_skips_allusers_row()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            scope: InstallScope.PerUser);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "ALLUSERS");
    }

    [Fact]
    public void Produce_with_restart_manager_emits_msirmshutdown_zero()
    {
        // MSIRMSHUTDOWN values (learn.microsoft.com/windows/win32/msi/msirmshutdown):
        //   0 = affected processes/services shut down unconditionally (chosen here).
        //   1 = same as 0, but unresponsive processes are FORCED to shut down.
        //   2 = affected processes/services shut down ONLY IF EVERY ONE of them
        //       has called Win32 RegisterApplicationRestart. Services always count
        //       as restartable (RM_PROCESS_INFO.bRestartable), but ordinary
        //       third-party apps rarely register, so "2" often shuts down nothing
        //       for them. "0" always shuts down, but per RmRestart's own docs only
        //       apps that registered for restart come back — unregistered apps
        //       stay closed.
        // Scope: this property is only consulted for silent (/qn) and basic-UI
        // (/qb) installs. At full UI, Windows Installer only asks Restart Manager
        // to act via an authored MsiRMFilesInUse dialog, which FalkForge does not
        // currently emit.
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: true);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "MSIRMSHUTDOWN", "0");
    }

    [Fact]
    public void Produce_without_restart_manager_skips_msirmshutdown()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: false);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "MSIRMSHUTDOWN");
    }

    // ── ICE34: RadioButtonGroup property needs a Property-table default ──────
    // A RadioButtonGroup control is not TAB-selectable and ICE34 fails validation unless the
    // group's bound property has a Property-table row whose value equals one of the group's
    // RadioButton values. MsiRMFilesInUseDlgBuilder's ShutdownOption group is bound to
    // FalkForgeRMOption, so this producer must default it whenever the dialog can be emitted.

    [Fact]
    public void Produce_with_restart_manager_emits_rm_option_default_matching_a_radio_button_value()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: true);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(
            rows,
            FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.OptionProperty,
            FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.UseRestartManagerValue);
    }

    [Fact]
    public void Produce_without_restart_manager_omits_rm_option()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: false);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(
            rows,
            r => ((CellValue.StringValue)r.Cells[0]).Value
                == FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.OptionProperty);
    }

    // ── DialogSet.None: MsiRMFilesInUse is never emitted, so its Property default must not
    // be either ── DialogSetProducer only appends the MsiRMFilesInUse dialog (and therefore its
    // ShutdownOption RadioButtonGroup control) when dialogSet != None. A package that enables
    // Restart Manager but selects DialogSet.None never authors that control, so seeding
    // FalkForgeRMOption regardless would ship an orphan Property row for a control that does not
    // exist — matching documentation.html's claim that the feature is inert for DialogSet.None.

    [Fact]
    public void Produce_with_restart_manager_and_dialogset_none_omits_rm_option()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: true,
            dialogSet: MsiDialogSet.None);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(
            rows,
            r => ((CellValue.StringValue)r.Cells[0]).Value
                == FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.OptionProperty);
    }

    [Fact]
    public void Produce_with_restart_manager_and_dialogset_minimal_emits_rm_option()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: true,
            dialogSet: MsiDialogSet.Minimal);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(
            rows,
            FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.OptionProperty,
            FalkForge.Compiler.Msi.UI.Layout.Builders.MsiRMFilesInUseDlgBuilder.UseRestartManagerValue);
    }

    [Fact]
    public void Produce_appends_user_properties_after_builtins()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "ARPCONTACT", Value = "support@example.com" },
            new PropertyModel { Name = "REINSTALLMODE", Value = "amus" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "ARPCONTACT", "support@example.com");
        AssertRow(rows, "REINSTALLMODE", "amus");
        AssertRow(rows, "ProductName", "T");
    }

    [Fact]
    public void Produce_user_property_overrides_builtin_value()
    {
        // Legacy EmitProperties writes builtins first, then user props into the same
        // dictionary, so a user-supplied ProductLanguage (for example) overrides.
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "ProductLanguage", Value = "1053" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "ProductLanguage", "1053");
        Assert.Single(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "ProductLanguage");
    }

    [Fact]
    public void Produce_skips_property_with_null_or_empty_value()
    {
        // PropertyModel.Value is non-nullable in the public surface; smuggle a null
        // through and also include an empty-string user value, both should be skipped.
        PropertyModel keep = new() { Name = "Keep", Value = "yes" };
        PropertyModel dropNull = new() { Name = "DropNull", Value = null! };
        PropertyModel dropEmpty = new() { Name = "DropEmpty", Value = string.Empty };

        ResolvedPackage resolved = MakeResolved(new[] { keep, dropNull, dropEmpty });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "Keep", "yes");
        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "DropNull");
        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "DropEmpty");
    }

    // ── SecureCustomProperties emission ──────────────────────────────────────

    [Fact]
    public void Produce_emits_securecustomproperties_ordinal_sorted()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "Z_PWD", Value = "z", IsSecure = true },
            new PropertyModel { Name = "A_PWD", Value = "a", IsSecure = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "A_PWD;Z_PWD");
    }

    [Fact]
    public void Produce_emits_securecustomproperties_for_secure_property_with_empty_value()
    {
        // Real-world shape: a secure property is normally declared with NO default value (the
        // runtime value is supplied only at install time, e.g. via SetSecureProperty). The
        // empty-value drop applied to ordinary rows below must not also drop the SECURE
        // membership itself, or every conventionally-declared secure property would silently
        // never reach SecureCustomProperties.
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "DB_PASSWORD", Value = "", IsSecure = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "DB_PASSWORD");
        // The property's OWN row is still dropped (empty value, NOT NULL column) —
        // only the aggregated membership list survives.
        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "DB_PASSWORD");
    }

    [Fact]
    public void Produce_omits_securecustomproperties_row_when_nothing_is_secure()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "PLAIN", Value = "x" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "SecureCustomProperties");
    }

    [Fact]
    public void Produce_securecustomproperties_excludes_unflagged_properties()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "SECRET", Value = "s", IsSecure = true },
            new PropertyModel { Name = "PLAIN", Value = "p" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "SECRET");
    }

    [Fact]
    public void Produce_securecustomproperties_deduplicates_repeated_secure_names()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "SECRET", Value = "s1", IsSecure = true },
            new PropertyModel { Name = "SECRET", Value = "s2", IsSecure = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "SECRET");
    }

    [Fact]
    public void Produce_securecustomproperties_has_no_trailing_semicolon()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "A_PWD", Value = "a", IsSecure = true },
            new PropertyModel { Name = "B_PWD", Value = "b", IsSecure = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "SecureCustomProperties");
        string value = ((CellValue.StringValue)row.Cells[1]).Value;
        Assert.False(value.EndsWith(';'));
        Assert.Equal("A_PWD;B_PWD", value);
    }

    [Fact]
    public void Produce_securecustomproperties_duplicate_name_last_declaration_wins()
    {
        // Matches the Value dictionary's own last-write-wins semantics for a repeated property
        // name (see the props[property.Name] = property.Value assignment above): when the same
        // name is declared twice, the LAST declaration's IsSecure flag determines membership,
        // not "secure if ANY declaration was secure."
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "SECRET", Value = "s1", IsSecure = true },
            new PropertyModel { Name = "SECRET", Value = "s2", IsSecure = false },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "SecureCustomProperties");
    }

    // ── AdminProperties emission ─────────────────────────────────────────────

    [Fact]
    public void Produce_emits_adminproperties_ordinal_sorted()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "Z_ADMIN", Value = "z", IsAdmin = true },
            new PropertyModel { Name = "A_ADMIN", Value = "a", IsAdmin = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "AdminProperties", "A_ADMIN;Z_ADMIN");
    }

    [Fact]
    public void Produce_omits_adminproperties_row_when_nothing_is_admin()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "PLAIN", Value = "x" },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "AdminProperties");
    }

    [Fact]
    public void Produce_adminproperties_allows_mixed_case_name()
    {
        // Unlike SecureCustomProperties (PRP001), AdminProperties explicitly permits mixed-case
        // (private) property names in MSI — this guards against copy-pasting the PRP001-style
        // uppercase check onto IsAdmin.
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "MixedCaseAdmin", Value = "x", IsAdmin = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "AdminProperties", "MixedCaseAdmin");
    }

    [Fact]
    public void Produce_securecustomproperties_and_adminproperties_are_independent()
    {
        ResolvedPackage resolved = MakeResolved(new[]
        {
            new PropertyModel { Name = "BOTH_FLAGS", Value = "x", IsSecure = true, IsAdmin = true },
            new PropertyModel { Name = "SECURE_ONLY", Value = "y", IsSecure = true },
            new PropertyModel { Name = "ADMIN_ONLY", Value = "z", IsAdmin = true },
        });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "BOTH_FLAGS;SECURE_ONLY");
        AssertRow(rows, "AdminProperties", "ADMIN_ONLY;BOTH_FLAGS");
    }

    // ── SecureCustomProperties must carry the Upgrade-table ActionProperty names (F1) ─────────
    // FindRelatedProducts runs client-side, unelevated, in InstallUISequence. Windows Installer
    // suppresses the duplicate InstallExecuteSequence copy of that action once the UI-sequence
    // copy has already run, so OLDERVERSIONFOUND/NEWERVERSIONFOUND only reach the elevated
    // server-side RemoveExistingProducts/LaunchConditions evaluation if they are listed in
    // SecureCustomProperties. Without this, any package installed through a UI (not /qn) never
    // actually upgrades — v2 installs side by side with v1 — even though the Upgrade table rows
    // and FindRelatedProducts scheduling (issue #65) are both correct.

    [Fact]
    public void Produce_with_upgrade_configured_adds_upgrade_action_properties_to_securecustomproperties()
    {
        ResolvedPackage resolved = MakeResolved(
            System.Array.Empty<PropertyModel>(),
            upgrade: new UpgradeModel());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "NEWERVERSIONFOUND;OLDERVERSIONFOUND");
    }

    [Fact]
    public void Produce_with_major_upgrade_configured_adds_upgrade_action_properties_to_securecustomproperties()
    {
        ResolvedPackage resolved = MakeResolved(
            System.Array.Empty<PropertyModel>(),
            majorUpgrade: new MajorUpgradeModel());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "NEWERVERSIONFOUND;OLDERVERSIONFOUND");
    }

    [Fact]
    public void Produce_without_upgrade_or_major_upgrade_does_not_add_upgrade_action_properties()
    {
        // PackageModel constructed directly here has Upgrade == null, MajorUpgrade == null — a
        // state PackageBuilder never actually produces (it always defaults Upgrade to a plain
        // UpgradeModel()), but a legitimate producer-contract case: with neither table source
        // configured, UpgradeTableProducer emits no Upgrade table at all, so there is nothing for
        // FindRelatedProducts to populate and no property to secure.
        ResolvedPackage resolved = MakeResolved(System.Array.Empty<PropertyModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.DoesNotContain(rows, r => ((CellValue.StringValue)r.Cells[0]).Value == "SecureCustomProperties");
    }

    [Fact]
    public void Produce_securecustomproperties_merges_upgrade_action_properties_with_user_declared_names_ordinal_sorted()
    {
        // Ordinal-sort determinism (required for Reproducible()) must hold with a mix of
        // user-declared secure properties and the two Upgrade-table names merged in.
        ResolvedPackage resolved = MakeResolved(
            new[]
            {
                new PropertyModel { Name = "Z_PWD", Value = "z", IsSecure = true },
                new PropertyModel { Name = "A_PWD", Value = "a", IsSecure = true },
            },
            upgrade: new UpgradeModel());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "A_PWD;NEWERVERSIONFOUND;OLDERVERSIONFOUND;Z_PWD");
    }

    [Fact]
    public void Produce_securecustomproperties_deduplicates_user_declared_upgrade_action_property_name()
    {
        // An author who has already declared OLDERVERSIONFOUND as a secure property themselves
        // must not end up with a duplicate entry — SortedSet merge, not append.
        ResolvedPackage resolved = MakeResolved(
            new[]
            {
                new PropertyModel { Name = "OLDERVERSIONFOUND", Value = "", IsSecure = true },
            },
            upgrade: new UpgradeModel());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "SecureCustomProperties", "NEWERVERSIONFOUND;OLDERVERSIONFOUND");
    }

    private static void AssertRow(ImmutableArray<RecipeRow> rows, string name, string value)
    {
        RecipeRow row = Assert.Single(
            rows,
            r => ((CellValue.StringValue)r.Cells[0]).Value == name);
        Assert.Equal(value, ((CellValue.StringValue)row.Cells[1]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new DictionaryStreamRegistry());
        PropertyTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<PropertyModel> properties,
        string name = "T",
        string manufacturer = "M",
        System.Version? version = null,
        System.Guid productCode = default,
        System.Guid upgradeCode = default,
        InstallScope scope = InstallScope.PerMachine,
        bool enableRestartManager = false,
        MsiDialogSet dialogSet = MsiDialogSet.Minimal,
        UpgradeModel? upgrade = null,
        MajorUpgradeModel? majorUpgrade = null)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = name,
                Manufacturer = manufacturer,
                Version = version ?? new System.Version(1, 0, 0),
                ProductCode = productCode,
                UpgradeCode = upgradeCode,
                Scope = scope,
                EnableRestartManager = enableRestartManager,
                DialogSet = dialogSet,
                Properties = properties,
                Upgrade = upgrade,
                MajorUpgrade = majorUpgrade,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
