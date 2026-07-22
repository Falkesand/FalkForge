using System;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
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
    // Expected UI table names emitted by any active dialog set.
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
        Assert.Equal(42, result.Value.Tables.Length);

        foreach (string name in UiTableNames)
        {
            Assert.Contains(result.Value.Tables, t => t.Name.Value == name);
        }
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

    private static RecipeTable GetTable(ImmutableArray<RecipeTable> tables, string name)
    {
        RecipeTable? table = tables.FirstOrDefault(t => t.Name.Value == name);
        Assert.NotNull(table);
        return table;
    }

    private static RecipeBuildContext MakeContext(MsiDialogSet dialogSet)
        => new(
            MakeResolvedPackage(dialogSet),
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
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
