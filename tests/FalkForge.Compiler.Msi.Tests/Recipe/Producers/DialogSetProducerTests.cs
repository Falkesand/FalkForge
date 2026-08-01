using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Compiler.Msi.Tests.UI.Layout;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Unit tests for <see cref="DialogSetProducer"/>. Covers the
/// <see cref="IMultiTableProducer"/> contract for MSI UI dialog tables:
/// None returns empty, each active dialog set emits the correct table names
/// with reasonable row counts, schema sanity per table, FK integrity between
/// Control and Dialog rows, and PK uniqueness within each table.
/// </summary>
public sealed class DialogSetProducerTests
{
    // Table names emitted by any active dialog set. RadioButton is deliberately NOT in this
    // list: unlike the other seven, it is only emitted when at least one dialog actually
    // authors a RadioButtonGroup row (see BuildDialogTables's remarks) — asserted separately by
    // the RadioButton-specific tests below.
    private static readonly string[] UiTableNames =
    [
        "Dialog",
        "Control",
        "ControlEvent",
        "ControlCondition",
        "EventMapping",
        "TextStyle",
        "UIText",
    ];

    // ── None → empty ──────────────────────────────────────────────────────────

    [Fact]
    public void Produce_with_DialogSet_None_returns_empty_array()
    {
        RecipeBuildContext context = MakeContext(MsiDialogSet.None);

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    // ── Interface contract ────────────────────────────────────────────────────

    [Fact]
    public void DialogSetProducer_implements_IMultiTableProducer()
    {
        Assert.IsAssignableFrom<IMultiTableProducer>(new DialogSetProducer());
    }

    // ── Minimal: correct table names ──────────────────────────────────────────

    [Fact]
    public void Produce_Minimal_emits_all_seven_ui_tables()
    {
        RecipeBuildContext context = MakeContext(MsiDialogSet.Minimal);

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);

        Assert.True(result.IsSuccess);
        ImmutableArray<RecipeTable> tables = result.Value;

        foreach (string name in UiTableNames)
        {
            Assert.Contains(tables, t => t.Name.Value == name);
        }
    }

    // ── RadioButton: only emitted when a dialog actually authors a row ────────
    // Unlike the seven tables above, RadioButton is NOT unconditional: an always-empty table
    // served no purpose and cost every package that never opts into Restart Manager (the only
    // current RadioButtonGroup author) a spurious extra table — churn a Reproducible() build has
    // no reason to carry for a package that never asked for the feature.

    [Fact]
    public void Produce_Minimal_without_restart_manager_omits_RadioButton_table()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);

        Assert.DoesNotContain(tables, t => t.Name.Value == "RadioButton");
    }

    [Fact]
    public void Produce_RadioButton_table_has_nine_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable radioButton = GetTable(tables, "RadioButton");

        // RadioButton DDL: Property, Order, Value, X, Y, Width, Height, Text, Help — nine
        // columns total: seven NOT-NULL geometry/key columns (Property, Order, Value, X, Y,
        // Width, Height) plus the two LOCALIZABLE Text/Help columns needed to carry
        // radio-button labels through to the Control-table style localization pass in
        // DialogSetProducer.Localization.cs. Easy to undercount as "seven" if you stop at the
        // NOT-NULL columns and forget the two LOCALIZABLE ones.
        Assert.Equal(9, radioButton.Columns.Length);
        Assert.Equal("Property", radioButton.Columns[0].Name);
        Assert.Equal("Order", radioButton.Columns[1].Name);
    }

    // ── Minimal: row count sanity ─────────────────────────────────────────────

    [Fact]
    public void Produce_Minimal_Dialog_table_has_at_least_one_row()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable dialog = GetTable(tables, "Dialog");

        // Minimal has Welcome + Progress + Exit + Cancel = at least 1 dialog row.
        Assert.NotEmpty(dialog.Rows);
    }

    [Fact]
    public void Produce_Minimal_TextStyle_table_has_five_rows()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable textStyle = GetTable(tables, "TextStyle");

        // DialogEmitter emits exactly 5 text styles: DlgFont8, DlgFontBold8,
        // DlgFont12, DlgFontBold12, VerdanaBold13.
        Assert.Equal(5, textStyle.Rows.Length);
    }

    [Fact]
    public void Produce_Minimal_UIText_table_has_twenty_three_rows()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable uiText = GetTable(tables, "UIText");

        // DialogEmitter.EmitUIText emits exactly 23 UIText entries.
        Assert.Equal(23, uiText.Rows.Length);
    }

    [Fact]
    public void Produce_Minimal_Control_table_references_WelcomeDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable control = GetTable(tables, "Control");

        // At least one Control row must reference the WelcomeDlg dialog.
        bool hasWelcomeControl = control.Rows.Any(r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "WelcomeDlg");

        Assert.True(hasWelcomeControl, "Control table must contain rows for WelcomeDlg.");
    }

    // ── InstallDir: includes InstallDirDlg ────────────────────────────────────

    [Fact]
    public void Produce_InstallDir_Dialog_table_includes_InstallDirDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.InstallDir);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "InstallDirDlg");
    }

    [Fact]
    public void Produce_InstallDir_emits_all_seven_ui_tables()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.InstallDir);

        foreach (string name in UiTableNames)
        {
            Assert.Contains(tables, t => t.Name.Value == name);
        }
    }

    // ── FeatureTree: includes CustomizeDlg ───────────────────────────────────

    [Fact]
    public void Produce_FeatureTree_Dialog_table_includes_CustomizeDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.FeatureTree);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "CustomizeDlg");
    }

    [Fact]
    public void Produce_FeatureTree_emits_all_seven_ui_tables()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.FeatureTree);

        foreach (string name in UiTableNames)
        {
            Assert.Contains(tables, t => t.Name.Value == name);
        }
    }

    // ── Mondo: superset includes SetupType + Customize + InstallDir ──────────

    [Fact]
    public void Produce_Mondo_Dialog_table_includes_SetupTypeDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Mondo);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "SetupTypeDlg");
    }

    [Fact]
    public void Produce_Mondo_Dialog_table_includes_CustomizeDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Mondo);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "CustomizeDlg");
    }

    [Fact]
    public void Produce_Mondo_Dialog_table_includes_InstallDirDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Mondo);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "InstallDirDlg");
    }

    [Fact]
    public void Produce_Mondo_Dialog_table_includes_BrowseDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Mondo);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "BrowseDlg");
    }

    [Fact]
    public void Produce_Mondo_has_more_dialogs_than_Minimal()
    {
        ImmutableArray<RecipeTable> minTables = ProduceTables(MsiDialogSet.Minimal);
        ImmutableArray<RecipeTable> mondoTables = ProduceTables(MsiDialogSet.Mondo);

        int minDialogCount = GetTable(minTables, "Dialog").Rows.Length;
        int mondoDialogCount = GetTable(mondoTables, "Dialog").Rows.Length;

        Assert.True(mondoDialogCount > minDialogCount,
            $"Mondo ({mondoDialogCount} dialogs) must have more than Minimal ({minDialogCount} dialogs).");
    }

    // ── Advanced: includes InstallScope ──────────────────────────────────────

    [Fact]
    public void Produce_Advanced_Dialog_table_includes_InstallScopeDlg()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Advanced);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "InstallScopeDlg");
    }

    // ── UIText: MenuAllLocal must not read the same as MenuLocal ──────────────
    // MenuAllLocal/MenuLocal drive the SelectionTree control's feature context menu: MenuLocal is
    // "install just this feature", MenuAllLocal is "install this feature AND its subfeatures". A
    // package with no custom LocalizationData resolves UIText from the built-in en-US culture, so
    // this exercises the real default en-US.json text an end user would see.

    [Fact]
    public void Produce_Mondo_UIText_MenuAllLocal_DiffersFromMenuLocal()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Mondo);
        RecipeTable uiText = GetTable(tables, "UIText");

        string MenuText(string key) => uiText.Rows
            .Where(r => r.Cells[0] is CellValue.StringValue sv && sv.Value == key)
            .Select(r => ((CellValue.StringValue)r.Cells[1]).Value)
            .Single();

        Assert.NotEqual(MenuText("MenuLocal"), MenuText("MenuAllLocal"));
    }

    // ── Schema: Dialog table has 10 columns ──────────────────────────────────

    [Fact]
    public void Produce_Minimal_Dialog_table_has_ten_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable dialog = GetTable(tables, "Dialog");

        // Dialog DDL: Dialog, HCentering, VCentering, Width, Height,
        //             Attributes, Title, Control_First, Control_Default, Control_Cancel
        Assert.Equal(10, dialog.Columns.Length);
        Assert.Equal("Dialog", dialog.Columns[0].Name);
        Assert.Equal("HCentering", dialog.Columns[1].Name);
    }

    [Fact]
    public void Produce_Minimal_Dialog_table_PK_is_column_zero()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Single(dialog.PrimaryKey);
        Assert.Equal(0, dialog.PrimaryKey[0].Value);
    }

    [Fact]
    public void Produce_Minimal_Control_table_has_twelve_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable control = GetTable(tables, "Control");

        // Control DDL: Dialog_, Control, Type, X, Y, Width, Height,
        //              Attributes, Property, Text, Control_Next, Help
        Assert.Equal(12, control.Columns.Length);
        Assert.Equal("Dialog_", control.Columns[0].Name);
        Assert.Equal("Control", control.Columns[1].Name);
    }

    [Fact]
    public void Produce_Minimal_ControlEvent_table_has_six_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable ce = GetTable(tables, "ControlEvent");

        // ControlEvent DDL: Dialog_, Control_, Event, Argument, Condition, Ordering
        Assert.Equal(6, ce.Columns.Length);
        Assert.Equal("Dialog_", ce.Columns[0].Name);
        Assert.Equal("Event", ce.Columns[2].Name);
    }

    [Fact]
    public void Produce_Minimal_TextStyle_table_has_five_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable ts = GetTable(tables, "TextStyle");

        // TextStyle DDL: TextStyle, FaceName, Size, Color, StyleBits
        Assert.Equal(5, ts.Columns.Length);
        Assert.Equal("TextStyle", ts.Columns[0].Name);
    }

    [Fact]
    public void Produce_Minimal_UIText_table_has_two_columns()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable uit = GetTable(tables, "UIText");

        // UIText DDL: Key, Text
        Assert.Equal(2, uit.Columns.Length);
        Assert.Equal("Key", uit.Columns[0].Name);
        Assert.Equal("Text", uit.Columns[1].Name);
    }

    // ── Schema: SQL strings non-empty and contain table name ─────────────────

    [Theory]
    [InlineData(MsiDialogSet.Minimal, "Dialog")]
    [InlineData(MsiDialogSet.Minimal, "Control")]
    [InlineData(MsiDialogSet.Minimal, "ControlEvent")]
    [InlineData(MsiDialogSet.Minimal, "TextStyle")]
    [InlineData(MsiDialogSet.Minimal, "UIText")]
    public void Produce_emitted_table_has_valid_sql_strings(MsiDialogSet dialogSet, string tableName)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        RecipeTable table = GetTable(tables, tableName);

        Assert.NotEmpty(table.CreateTableSql);
        Assert.Contains(tableName, table.CreateTableSql, StringComparison.Ordinal);
        Assert.NotEmpty(table.InsertViewSql);
        Assert.Contains(tableName, table.InsertViewSql, StringComparison.Ordinal);
        Assert.Contains("SELECT", table.InsertViewSql, StringComparison.OrdinalIgnoreCase);
    }

    // ── FK integrity: all Control.Dialog_ values reference an emitted Dialog ──

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_Control_Dialog_references_all_exist_in_Dialog_table(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        RecipeTable dialogTable = GetTable(tables, "Dialog");
        RecipeTable controlTable = GetTable(tables, "Control");

        // Collect all emitted dialog names.
        var dialogNames = dialogTable.Rows
            .Select(r => ((CellValue.StringValue)r.Cells[0]).Value)
            .ToHashSet(StringComparer.Ordinal);

        // Every Control row's Dialog_ cell (col 0) must be in dialogNames.
        foreach (RecipeRow row in controlTable.Rows)
        {
            string dialogRef = ((CellValue.StringValue)row.Cells[0]).Value;
            Assert.Contains(dialogRef, dialogNames);
        }
    }

    // ── PK uniqueness within each table ──────────────────────────────────────

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.Mondo)]
    public void Produce_Dialog_table_PK_values_are_unique(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        RecipeTable dialog = GetTable(tables, "Dialog");

        var names = dialog.Rows
            .Select(r => ((CellValue.StringValue)r.Cells[0]).Value)
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.Mondo)]
    public void Produce_TextStyle_table_PK_values_are_unique(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        RecipeTable ts = GetTable(tables, "TextStyle");

        var keys = ts.Rows
            .Select(r => ((CellValue.StringValue)r.Cells[0]).Value)
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    // ── MsiRecipeBuilder integration ──────────────────────────────────────────

    [Fact]
    public void MsiRecipeBuilder_with_DialogSetProducer_Minimal_appends_seven_ui_tables()
    {
        ResolvedPackage resolved = MakeResolvedPackage(MsiDialogSet.Minimal);

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            [new DialogSetProducer()]);

        Assert.True(result.IsSuccess);

        // 35 built-in tables (Lock* suppressed for no-permission package) + 7 UI tables = 42.
        // No RadioButton table: this package never enables Restart Manager, so no dialog
        // authors a RadioButtonGroup row (see BuildDialogTables's remarks).
        Assert.Equal(42, result.Value.Tables.Length);

        foreach (string name in UiTableNames)
        {
            Assert.Contains(result.Value.Tables, t => t.Name.Value == name);
        }

        Assert.DoesNotContain(result.Value.Tables, t => t.Name.Value == "RadioButton");
    }

    [Fact]
    public void MsiRecipeBuilder_with_DialogSetProducer_Minimal_and_restart_manager_appends_eight_ui_tables_including_RadioButton()
    {
        ResolvedPackage resolved = new()
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                DialogSet = MsiDialogSet.Minimal,
                EnableRestartManager = true,
            },
            Components = [],
            Files = [],
        };

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            [new DialogSetProducer()]);

        Assert.True(result.IsSuccess);

        // 35 built-in tables + 7 always-present UI tables + RadioButton (populated, since
        // MsiRMFilesInUse's ShutdownOption group authors two rows) = 43.
        Assert.Equal(43, result.Value.Tables.Length);

        RecipeTable radioButton = GetTable(result.Value.Tables, "RadioButton");
        Assert.Equal(2, radioButton.Rows.Length);
    }

    [Fact]
    public void MsiRecipeBuilder_with_DialogSetProducer_None_does_not_append_ui_tables()
    {
        ResolvedPackage resolved = MakeResolvedPackage(MsiDialogSet.None);

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            [],
            new MsiRecipeBuildOptions(),
            [new DialogSetProducer()]);

        Assert.True(result.IsSuccess);

        // 35 built-in tables only (Lock* suppressed for no-permission package) — no UI tables appended.
        Assert.Equal(35, result.Value.Tables.Length);
    }

    // ── Well-known TextStyle rows ─────────────────────────────────────────────

    [Fact]
    public void Produce_Minimal_TextStyle_contains_DlgFont8()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable ts = GetTable(tables, "TextStyle");

        Assert.Contains(ts.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "DlgFont8");
    }

    [Fact]
    public void Produce_Minimal_TextStyle_contains_VerdanaBold13()
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(MsiDialogSet.Minimal);
        RecipeTable ts = GetTable(tables, "TextStyle");

        Assert.Contains(ts.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "VerdanaBold13");
    }

    // ── Custom LocalizationData must not drop the built-in Dialog.RestartManager.* defaults ──
    // Regression guard: DialogSetProducer.Localization.cs seeds UiTextLocDefaults under a
    // package's own LocalizationData (built-in en-US/sv-SE strings are otherwise dropped
    // entirely once an author supplies custom localization). Without also seeding the five
    // Dialog.RestartManager.* keys there, a package combining custom LocalizationData with
    // EnableRestartManagerSupport() would hit an unresolvable "!(loc.Dialog.RestartManager.*)"
    // reference and fail the build — a previously-working package breaking silently.

    [Fact]
    public void Produce_with_custom_localization_still_resolves_restart_manager_defaults()
    {
        var customDialog = new CustomDialogModel
        {
            Id = "RmFilesInUseDlg",
            Controls =
            [
                new CustomDialogControlModel
                {
                    Name = "TitleText",
                    Type = CustomControlType.Text,
                    X = 0,
                    Y = 0,
                    Width = 200,
                    Height = 10,
                    Text = "!(loc.Dialog.RestartManager.Title)",
                },
            ],
        };

        var package = new PackageModel
        {
            Name = "Test",
            Manufacturer = "M",
            Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.None,
            EnableRestartManager = true,
            CustomDialogs = [customDialog],
            LocalizationData =
            [
                new LocalizationData
                {
                    // Author supplies their own culture, but never translates the framework's
                    // Dialog.RestartManager.* keys — the seeded defaults must still resolve them.
                    Culture = "en-US",
                    Strings = new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["Button.OK"] = "OK",
                    },
                },
            ],
        };

        RecipeBuildContext context = new(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        RecipeTable control = GetTable(result.Value, "Control");
        RecipeRow row = control.Rows.Single(r =>
            r.Cells[1] is CellValue.StringValue sv && sv.Value == "TitleText");

        var text = (CellValue.StringValue)row.Cells[9];
        Assert.Equal("Files In Use", text.Value);
    }

    // ── MsiRMFilesInUse: appended per-package, gated on EnableRestartManager ──
    // This dialog is not part of any of the five stock templates (it is created by the
    // installer engine directly at InstallValidate, reachable from no other dialog), so it is
    // appended by the producer itself rather than by IDialogTemplate.GetDialogs.

    [Fact]
    public void Produce_with_restart_manager_enabled_emits_MsiRMFilesInUse_dialog_row()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "MsiRMFilesInUse");
    }

    [Fact]
    public void Produce_without_restart_manager_omits_MsiRMFilesInUse_dialog_row()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: false);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.DoesNotContain(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "MsiRMFilesInUse");
    }

    [Fact]
    public void Produce_with_restart_manager_enabled_emits_two_RadioButton_rows()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable radioButton = GetTable(tables, "RadioButton");

        Assert.Equal(2, radioButton.Rows.Length);
    }

    [Fact]
    public void Produce_with_DialogSet_None_omits_MsiRMFilesInUse_even_when_rm_enabled()
    {
        RecipeBuildContext context = new(
            new ResolvedPackage
            {
                Package = new PackageModel
                {
                    Name = "Test",
                    Manufacturer = "M",
                    Version = new Version(1, 0, 0),
                    DialogSet = MsiDialogSet.None,
                    EnableRestartManager = true,
                },
                Components = [],
                Files = [],
            },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Produce_with_restart_manager_emits_rm_shutdown_control_event_with_use_rm_condition()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable controlEvent = GetTable(tables, "ControlEvent");

        RecipeRow row = controlEvent.Rows.Single(r =>
            r.Cells[0] is CellValue.StringValue dlg && dlg.Value == "MsiRMFilesInUse" &&
            r.Cells[2] is CellValue.StringValue ev && ev.Value == "RMShutdownAndRestart");

        var condition = (CellValue.StringValue)row.Cells[4];
        Assert.Equal("FalkForgeRMOption~=\"UseRM\"", condition.Value);
    }

    [Fact]
    public void Produce_resolves_radio_button_label_localization()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable radioButton = GetTable(tables, "RadioButton");

        // Row-count assertion first: with zero rows the loop below (and the original version of
        // this test, before this fix) passes vacuously without ever exercising resolution. Then
        // pin the actual resolved en-US text (from Localization/en-US.json), not just "not still
        // a !(loc.…) reference" — a resolver bug that substitutes the WRONG key's text would still
        // satisfy a "no !(loc." check.
        Assert.Equal(2, radioButton.Rows.Length);

        var useRmText = (CellValue.StringValue)radioButton.Rows[0].Cells[7];
        Assert.Equal("Close the applications and attempt to &restart them.", useRmText.Value);

        var dontUseRmText = (CellValue.StringValue)radioButton.Rows[1].Cells[7];
        Assert.Equal("&Do not close applications. A reboot will be required to complete setup.", dontUseRmText.Value);
    }

    // ── RadioButton: full cell-for-cell mapping ───────────────────────────────
    // Regression guard for a swapped-column mutation: DialogSetProducer.Rows.cs writes nine cells
    // per RadioButton row in a fixed order (Property, Order, Value, X, Y, Width, Height, Text,
    // Help). Before this test, only the row COUNT, the loc-freeness of Cells[7], and the column
    // NAMES were asserted anywhere — a mutation that swapped the X and Width cells (rows would
    // emit X=295, Width=0, and both radio options render zero-width, so the user can select
    // neither) sailed through every existing test, msi.dll (both are SHORT columns), and ICE34/
    // ICE17 (neither inspects geometry). This test reads every geometry/key cell explicitly, so
    // that mutation now fails it.

    [Fact]
    public void Produce_with_restart_manager_emits_radio_button_rows_with_correct_cell_mapping()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable radioButton = GetTable(tables, "RadioButton");

        Assert.Equal(2, radioButton.Rows.Length);

        // Pinned against MsiRMFilesInUseDlgBuilder.Build()'s own radioButtons array — the intent
        // the builder authors, not a value guessed independently of it.
        AssertRadioButtonRow(
            radioButton.Rows[0],
            property: "FalkForgeRMOption", order: 1, value: "UseRM",
            x: 0, y: 0, width: 295, height: 16);

        AssertRadioButtonRow(
            radioButton.Rows[1],
            property: "FalkForgeRMOption", order: 2, value: "DontUseRM",
            x: 0, y: 20, width: 295, height: 16);
    }

    private static void AssertRadioButtonRow(
        RecipeRow row, string property, int order, string value, int x, int y, int width, int height)
    {
        Assert.Equal(property, ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal(order, ((CellValue.IntValue)row.Cells[1]).Value);
        Assert.Equal(value, ((CellValue.StringValue)row.Cells[2]).Value);
        Assert.Equal(x, ((CellValue.IntValue)row.Cells[3]).Value);
        Assert.Equal(y, ((CellValue.IntValue)row.Cells[4]).Value);
        Assert.Equal(width, ((CellValue.IntValue)row.Cells[5]).Value);
        Assert.Equal(height, ((CellValue.IntValue)row.Cells[6]).Value);

        // Help (Cells[8]) is always null — see DialogSetProducer.Rows.cs's RadioButton row build.
        Assert.IsType<CellValue.Null>(row.Cells[8]);
    }

    // ── ControlEvent: EndDialog Condition must never be blank ─────────────────
    // Per the ControlEvent table docs: "The installer does not trigger an event with a blank in
    // the Condition field unless no other events of the control evaluate to True." A NULL
    // condition on OK/EndDialog would mean OK does not close the dialog whenever the default
    // UseRM option is selected (the RMShutdownAndRestart event's condition is the only other
    // event on OK, and it is false once RM already ran and the property no longer matches
    // however the state evolves) — it only works today because DialogComposer.cs and
    // DialogSetProducer.Rows.cs both coalesce a null Condition to "1". This test pins the emitted
    // value directly so a future change that reads WiX's own .wxs (which omits Condition on this
    // Publish) cannot silently reintroduce a blank condition here.

    [Fact]
    public void Produce_with_restart_manager_emits_end_dialog_rows_with_condition_one()
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(MsiDialogSet.Minimal, enableRestartManager: true);
        RecipeTable controlEvent = GetTable(tables, "ControlEvent");

        var endDialogRows = controlEvent.Rows
            .Where(r =>
                r.Cells[0] is CellValue.StringValue dlg && dlg.Value == "MsiRMFilesInUse" &&
                r.Cells[2] is CellValue.StringValue ev && ev.Value == "EndDialog")
            .ToList();

        // OK's EndDialog and Cancel's EndDialog — both must carry Condition "1".
        Assert.Equal(2, endDialogRows.Count);
        foreach (RecipeRow row in endDialogRows)
        {
            var condition = (CellValue.StringValue)row.Cells[4];
            Assert.Equal("1", condition.Value);
        }
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_MsiRMFilesInUse_emitted_for_every_stock_dialog_set(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(dialogSet, enableRestartManager: true);
        RecipeTable dialog = GetTable(tables, "Dialog");

        Assert.Contains(dialog.Rows, r =>
            r.Cells[0] is CellValue.StringValue sv &&
            sv.Value == "MsiRMFilesInUse");
    }

    // ── Tab cycle: every emitted dialog has ONE closed Control_Next ring ─────────────────────
    // No ICE validates Control_Next here (ICE03 only checks it via the _Validation table, which
    // this repo does not author), so these row-level graph-invariant checks — read straight off
    // the emitted Control rows, not the pre-emission MsiDialogModel — are the only gate protecting
    // keyboard navigation across every stock dialog set.

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_every_dialog_has_a_single_closed_tab_cycle(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        AssertAllDialogsHaveSingleClosedCycle(tables);
    }

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_with_restart_manager_every_dialog_including_MsiRMFilesInUse_has_a_single_closed_tab_cycle(
        MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTablesWithRestartManager(dialogSet, enableRestartManager: true);
        AssertAllDialogsHaveSingleClosedCycle(tables);
    }

    // ── Tab cycle: Control_First always names a focusable control of the dialog's own ────────
    // Control_First is left exactly as authored (never derived from the cycle), so this is an
    // invariant test rather than a value-pinning one: whichever control the dialog names as its
    // entry point must actually exist as a Control row on that same dialog AND must be a
    // focusable type. Naming a Text/Line/etc. control here would mean focus lands on a control
    // with no Control_Next successor and Tab goes nowhere — exactly the defect this producer's
    // tab-cycle wiring exists to fix (see ADR 0007 section 2, which names this test as the
    // guarantee: it must actually assert focusability, not merely row existence).

    // Duplicated ON PURPOSE from DialogTabCycle's exclusion set — see TabCycleAssert's remarks on
    // why a test-owned copy, not a shared reference, catches a WRONG exclusion list instead of
    // agreeing with it.
    private static readonly HashSet<string> NonFocusableTypeNames = new(StringComparer.Ordinal)
    {
        "Text", "Line", "Bitmap", "Icon", "ProgressBar", "GroupBox", "VolumeCostList",
    };

    [Theory]
    [InlineData(MsiDialogSet.Minimal)]
    [InlineData(MsiDialogSet.InstallDir)]
    [InlineData(MsiDialogSet.FeatureTree)]
    [InlineData(MsiDialogSet.Mondo)]
    [InlineData(MsiDialogSet.Advanced)]
    public void Produce_Control_First_names_a_control_in_the_tab_cycle(MsiDialogSet dialogSet)
    {
        ImmutableArray<RecipeTable> tables = ProduceTables(dialogSet);
        RecipeTable dialog = GetTable(tables, "Dialog");
        RecipeTable control = GetTable(tables, "Control");

        foreach (RecipeRow row in dialog.Rows)
        {
            string dialogName = Str(row.Cells[0]);
            string controlFirst = Str(row.Cells[7]);

            RecipeRow? firstControlRow = control.Rows.FirstOrDefault(r =>
                Str(r.Cells[0]) == dialogName && Str(r.Cells[1]) == controlFirst);

            Assert.True(
                firstControlRow is not null,
                $"Dialog '{dialogName}' declares Control_First '{controlFirst}', which is not one of its own Control rows.");

            string controlFirstType = Str(firstControlRow.Cells[2]);
            Assert.False(
                NonFocusableTypeNames.Contains(controlFirstType),
                $"Dialog '{dialogName}' declares Control_First '{controlFirst}' as a {controlFirstType} control, which cannot receive keyboard focus.");
        }
    }

    // ── Tab cycle: deterministic across repeated builds ───────────────────────────────────────
    // Nothing existing pins this: DialogTabCycle.Assign's geometric sort uses an explicit
    // Comparison<int> rather than relying on sort stability, but a Reproducible() build still
    // needs the emitted (Dialog_, Control, Control_Next) triples to be byte-identical run to run.

    [Fact]
    public void Produce_tab_cycle_is_identical_across_repeated_builds()
    {
        ImmutableArray<RecipeTable> first = ProduceTablesWithRestartManager(MsiDialogSet.Mondo, enableRestartManager: true);
        ImmutableArray<RecipeTable> second = ProduceTablesWithRestartManager(MsiDialogSet.Mondo, enableRestartManager: true);

        var firstChain = ControlNextChain(first);
        var secondChain = ControlNextChain(second);

        Assert.Equal(firstChain, secondChain);
    }

    private static List<(string Dialog, string Control, string? Next)> ControlNextChain(ImmutableArray<RecipeTable> tables)
        => GetTable(tables, "Control").Rows
            .Select(r => (
                Dialog: Str(r.Cells[0]),
                Control: Str(r.Cells[1]),
                Next: r.Cells[10] is CellValue.StringValue sv ? sv.Value : null))
            .ToList();

    // ── Tab cycle: custom dialogs and extension-inserted steps are wired too ─────────────────
    // Produce_every_dialog_has_a_single_closed_tab_cycle (above) only ever builds dialogs from
    // stock templates (via ProduceTables) or from stock templates + MsiRMFilesInUse (via
    // ProduceTablesWithRestartManager) — neither populates PackageModel.CustomDialogs nor passes
    // extension step builders. DialogSetProducer.Produce's tab-cycle Assign loop runs AFTER all
    // four dialog sources converge (stock templates, MsiRMFilesInUse, custom dialogs, extension
    // steps — see Produce's remarks), so these two tests close the coverage gap: without them, a
    // regression that moved the Assign loop earlier (before custom dialogs / extension steps are
    // appended) would revert every custom and extension-step dialog to an all-NULL Control_Next
    // chain while the whole suite stayed green.

    [Fact]
    public void Produce_custom_dialog_without_authored_chain_gets_an_automatic_tab_cycle()
    {
        var customDialog = new CustomDialogModel
        {
            Id = "AutoWireDlg",
            Controls =
            [
                new CustomDialogControlModel
                {
                    Name = "Prompt", Type = CustomControlType.Text,
                    X = 20, Y = 20, Width = 330, Height = 20, Text = "Enter a value:",
                },
                new CustomDialogControlModel
                {
                    Name = "ValueEdit", Type = CustomControlType.Edit,
                    X = 20, Y = 50, Width = 330, Height = 18, Property = "VALUE",
                },
                new CustomDialogControlModel
                {
                    Name = "Next", Type = CustomControlType.PushButton,
                    X = 280, Y = 240, Width = 66, Height = 17, Text = "Next",
                },
            ],
        };

        var package = new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.None,
            CustomDialogs = [customDialog],
        };

        RecipeBuildContext context = new(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        RecipeTable control = GetTable(result.Value, "Control");
        var controls = control.Rows
            .Where(r => Str(r.Cells[0]) == "AutoWireDlg")
            .Select(r => (
                Name: Str(r.Cells[1]),
                TypeName: Str(r.Cells[2]),
                Next: r.Cells[10] is CellValue.StringValue sv ? sv.Value : null))
            .ToList();

        TabCycleAssert.AssertSingleClosedCycle(controls);
    }

    [Fact]
    public void Produce_extension_step_dialog_without_authored_chain_gets_an_automatic_tab_cycle()
    {
        var customization = new DialogCustomizationModel
        {
            InsertedSteps = ImmutableArray.Create(new InsertedDialogStep("AutoWireStep", StockDialog.Welcome)),
        };
        var package = new PackageModel
        {
            Name = "App", Manufacturer = "M", Version = new Version(1, 0, 0),
            DialogSet = MsiDialogSet.None,
            DialogCustomization = customization,
        };

        RecipeBuildContext context = new(
            new ResolvedPackage { Package = package, Components = [], Files = [] },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result =
            new DialogSetProducer([new AutoWireStepBuilder()]).Produce(context);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);

        RecipeTable control = GetTable(result.Value, "Control");
        var controls = control.Rows
            .Where(r => Str(r.Cells[0]) == "AutoWireStep")
            .Select(r => (
                Name: Str(r.Cells[1]),
                TypeName: Str(r.Cells[2]),
                Next: r.Cells[10] is CellValue.StringValue sv ? sv.Value : null))
            .ToList();

        TabCycleAssert.AssertSingleClosedCycle(controls);
    }

    private sealed class AutoWireStepBuilder : IMsiDialogStepBuilder
    {
        public string Name => "AutoWireStep";

        public MsiDialogModel Build(DialogBuildContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var model = new MsiDialogModel { Name = Name, FirstControl = "Body" };
            model.Controls.Add(new MsiControlModel
            {
                Name = "Body", Type = MsiControlType.PushButton, X = 10, Y = 10, Width = 100, Height = 20, Text = "x",
            });
            model.Controls.Add(new MsiControlModel
            {
                Name = "Other", Type = MsiControlType.PushButton, X = 10, Y = 40, Width = 100, Height = 20, Text = "y",
            });
            return model;
        }
    }

    private static void AssertAllDialogsHaveSingleClosedCycle(ImmutableArray<RecipeTable> tables)
    {
        RecipeTable control = GetTable(tables, "Control");

        var byDialog = control.Rows
            .GroupBy(r => Str(r.Cells[0]), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(string Name, string TypeName, string? Next)>)g
                    .Select(r => (
                        Name: Str(r.Cells[1]),
                        TypeName: Str(r.Cells[2]),
                        Next: r.Cells[10] is CellValue.StringValue sv ? sv.Value : null))
                    .ToList(),
                StringComparer.Ordinal);

        foreach (var controls in byDialog.Values)
        {
            TabCycleAssert.AssertSingleClosedCycle(controls);
        }
    }

    private static string Str(CellValue cell) => ((CellValue.StringValue)cell).Value;

    // ── Helpers ───────────────────────────────────────────────────────────────
    // Multi-culture localization is now realized as per-culture MST transforms by MsiAuthoring
    // (see MsiAuthoringLocalizationTests); the producer no longer queues a DLG005 "dropped" warning.

    private static ImmutableArray<RecipeTable> ProduceTables(MsiDialogSet dialogSet)
    {
        RecipeBuildContext context = MakeContext(dialogSet);
        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);
        Assert.True(result.IsSuccess, "DialogSetProducer.Produce failed");
        return result.Value;
    }

    private static ImmutableArray<RecipeTable> ProduceTablesWithRestartManager(MsiDialogSet dialogSet, bool enableRestartManager)
    {
        RecipeBuildContext context = new(
            new ResolvedPackage
            {
                Package = new PackageModel
                {
                    Name = "Test",
                    Manufacturer = "M",
                    Version = new Version(1, 0, 0),
                    DialogSet = dialogSet,
                    EnableRestartManager = enableRestartManager,
                },
                Components = [],
                Files = [],
            },
            new DictionaryStreamRegistry());

        Result<ImmutableArray<RecipeTable>> result = new DialogSetProducer().Produce(context);
        Assert.True(result.IsSuccess, "DialogSetProducer.Produce failed");
        return result.Value;
    }

    private static RecipeTable GetTable(ImmutableArray<RecipeTable> tables, string name)
    {
        RecipeTable? table = tables.FirstOrDefault(t => t.Name.Value == name);
        Assert.NotNull(table);
        return table;
    }

    private static RecipeBuildContext MakeContext(MsiDialogSet dialogSet)
        => new(
            MakeResolvedPackage(dialogSet),
            new DictionaryStreamRegistry());

    private static ResolvedPackage MakeResolvedPackage(MsiDialogSet dialogSet)
        => new()
        {
            Package = new PackageModel
            {
                Name = "Test",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                DialogSet = dialogSet,
            },
            Components = [],
            Files = [],
        };
}
