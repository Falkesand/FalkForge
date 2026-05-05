using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class InstallExecuteSequenceTableProducerTests
{
    // ── Schema ───────────────────────────────────────────────────────────────

    [Fact]
    public void Schema_name_is_InstallExecuteSequence()
    {
        InstallExecuteSequenceTableProducer producer = new();

        Assert.Equal("InstallExecuteSequence", producer.Schema.Name.Value);
    }

    [Fact]
    public void Schema_has_three_columns_matching_msi_table_definitions()
    {
        // DDL: Action CHAR(72) NN, Condition CHAR(255) nullable, Sequence SHORT nullable.
        InstallExecuteSequenceTableProducer producer = new();
        ImmutableArray<RecipeColumn> cols = producer.Schema.Columns;

        Assert.Equal(3, cols.Length);
        Assert.Equal("Action",    cols[0].Name);
        Assert.Equal("Condition", cols[1].Name);
        Assert.Equal("Sequence",  cols[2].Name);

        Assert.Equal(ColumnType.String,  cols[0].Type);
        Assert.Equal(ColumnType.String,  cols[1].Type);
        Assert.Equal(ColumnType.Integer, cols[2].Type);

        Assert.Equal(72,  cols[0].Width);
        Assert.Equal(255, cols[1].Width);
        Assert.Equal(2,   cols[2].Width);

        Assert.False(cols[0].Nullable);
        Assert.True(cols[1].Nullable);
        Assert.True(cols[2].Nullable);
    }

    [Fact]
    public void Schema_primary_key_is_Action_column_index_zero()
    {
        InstallExecuteSequenceTableProducer producer = new();

        ColumnIndex pk = Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, pk.Value);
    }

    // ── Empty package → baseline only ────────────────────────────────────────

    [Fact]
    public void Produce_empty_package_returns_all_unconditional_baseline_actions()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "TestPkg", Manufacturer = "M", Version = new Version(1, 0, 0),
        }));
        IReadOnlyList<string> names = ActionNames(rows);

        Assert.Contains("AppSearch",            names);
        Assert.Contains("LaunchConditions",     names);
        Assert.Contains("ValidateProductID",    names);
        Assert.Contains("CostInitialize",       names);
        Assert.Contains("FileCost",             names);
        Assert.Contains("CostFinalize",         names);
        Assert.Contains("InstallValidate",      names);
        Assert.Contains("InstallInitialize",    names);
        Assert.Contains("ProcessComponents",    names);
        Assert.Contains("UnpublishFeatures",    names);
        Assert.Contains("RemoveRegistryValues", names);
        Assert.Contains("RemoveShortcuts",      names);
        Assert.Contains("RemoveFiles",          names);
        Assert.Contains("InstallFiles",         names);
        Assert.Contains("CreateShortcuts",      names);
        Assert.Contains("WriteRegistryValues",  names);
        Assert.Contains("RegisterUser",         names);
        Assert.Contains("RegisterProduct",      names);
        Assert.Contains("PublishFeatures",      names);
        Assert.Contains("PublishProduct",       names);
        Assert.Contains("InstallFinalize",      names);
    }

    [Fact]
    public void Produce_empty_package_does_not_emit_conditional_actions()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "TestPkg", Manufacturer = "M", Version = new Version(1, 0, 0),
        }));
        IReadOnlyList<string> names = ActionNames(rows);

        Assert.DoesNotContain("RegisterFonts",            names);
        Assert.DoesNotContain("UnregisterFonts",          names);
        Assert.DoesNotContain("WriteIniValues",           names);
        Assert.DoesNotContain("RemoveIniValues",          names);
        Assert.DoesNotContain("RegisterExtensionInfo",    names);
        Assert.DoesNotContain("UnregisterExtensionInfo",  names);
        Assert.DoesNotContain("RemoveExistingProducts",   names);
        Assert.DoesNotContain("MigrateFeatureStates",     names);
        Assert.DoesNotContain("RemoveEnvironmentStrings", names);
        Assert.DoesNotContain("WriteEnvironmentStrings",  names);
        Assert.DoesNotContain("StopServices",             names);
        Assert.DoesNotContain("DeleteServices",           names);
        Assert.DoesNotContain("InstallServices",          names);
        Assert.DoesNotContain("StartServices",            names);
        Assert.DoesNotContain("CreateFolders",            names);
        Assert.DoesNotContain("RemoveFolders",            names);
        Assert.DoesNotContain("MoveFiles",                names);
        Assert.DoesNotContain("DuplicateFiles",           names);
        Assert.DoesNotContain("RemoveDuplicateFiles",     names);
    }

    // ── Baseline sequence numbers ─────────────────────────────────────────────

    [Fact]
    public void Produce_baseline_actions_have_correct_sequence_numbers()
    {
        // Mirrors legacy TableEmitter.EmitInstallSequences baseline list exactly.
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "TestPkg", Manufacturer = "M", Version = new Version(1, 0, 0),
        })));

        Assert.Equal(50,   seq["AppSearch"]);
        Assert.Equal(100,  seq["LaunchConditions"]);
        Assert.Equal(700,  seq["ValidateProductID"]);
        Assert.Equal(800,  seq["CostInitialize"]);
        Assert.Equal(900,  seq["FileCost"]);
        Assert.Equal(1000, seq["CostFinalize"]);
        Assert.Equal(1400, seq["InstallValidate"]);
        Assert.Equal(1500, seq["InstallInitialize"]);
        Assert.Equal(1600, seq["ProcessComponents"]);
        Assert.Equal(1800, seq["UnpublishFeatures"]);
        Assert.Equal(2600, seq["RemoveRegistryValues"]);
        Assert.Equal(3200, seq["RemoveShortcuts"]);
        Assert.Equal(3500, seq["RemoveFiles"]);
        Assert.Equal(4000, seq["InstallFiles"]);
        Assert.Equal(4500, seq["CreateShortcuts"]);
        Assert.Equal(5000, seq["WriteRegistryValues"]);
        Assert.Equal(6000, seq["RegisterUser"]);
        Assert.Equal(6100, seq["RegisterProduct"]);
        Assert.Equal(6300, seq["PublishFeatures"]);
        Assert.Equal(6400, seq["PublishProduct"]);
        Assert.Equal(6600, seq["InstallFinalize"]);
    }

    [Fact]
    public void Produce_rows_are_sorted_ascending_by_sequence_number()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "TestPkg", Manufacturer = "M", Version = new Version(1, 0, 0),
        }));
        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        Assert.Equal(seqs.OrderBy(x => x).ToArray(), seqs);
    }

    [Fact]
    public void Produce_baseline_action_condition_cells_are_empty_string()
    {
        // Legacy TableEmitter.EmitInstallSequences writes "" (empty string) for baseline
        // conditions — not null. Producer must match to achieve byte-level parity.
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "TestPkg", Manufacturer = "M", Version = new Version(1, 0, 0),
        }));

        foreach (RecipeRow row in rows)
        {
            CellValue.StringValue condCell = Assert.IsType<CellValue.StringValue>(row.Cells[1]);
            Assert.Equal(string.Empty, condCell.Value);
        }
    }

    // ── Conditional: Fonts ────────────────────────────────────────────────────

    [Fact]
    public void Produce_with_fonts_emits_RegisterFonts_and_UnregisterFonts()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            Fonts = new List<FontModel> { new FontModel { FileName = "Arial.ttf" } },
        })));

        Assert.Contains("RegisterFonts",   names);
        Assert.Contains("UnregisterFonts", names);
    }

    [Fact]
    public void Produce_with_fonts_RegisterFonts_at_5300_UnregisterFonts_at_3100()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            Fonts = new List<FontModel> { new FontModel { FileName = "f.ttf" } },
        })));

        Assert.Equal(5300, seq["RegisterFonts"]);
        Assert.Equal(3100, seq["UnregisterFonts"]);
    }

    // ── Conditional: IniFiles ─────────────────────────────────────────────────

    [Fact]
    public void Produce_with_ini_files_emits_WriteIniValues_and_RemoveIniValues()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            IniFiles = new List<IniFileModel>
            {
                new IniFileModel { FileName = "app.ini", Section = "S", Key = "K", Value = "V" },
            },
        })));

        Assert.Contains("WriteIniValues",  names);
        Assert.Contains("RemoveIniValues", names);
    }

    [Fact]
    public void Produce_with_ini_files_WriteIniValues_at_5100_RemoveIniValues_at_3400()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            IniFiles = new List<IniFileModel>
            {
                new IniFileModel { FileName = "app.ini", Section = "S", Key = "K", Value = "V" },
            },
        })));

        Assert.Equal(5100, seq["WriteIniValues"]);
        Assert.Equal(3400, seq["RemoveIniValues"]);
    }

    // ── Conditional: FileAssociations ─────────────────────────────────────────

    [Fact]
    public void Produce_with_file_associations_emits_extension_info_actions()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            FileAssociations = new List<FileAssociationModel>
            {
                new FileAssociationModel { Extension = ".txt", ProgId = "App.Text" },
            },
        })));

        Assert.Contains("RegisterExtensionInfo",   names);
        Assert.Contains("UnregisterExtensionInfo", names);
    }

    [Fact]
    public void Produce_with_file_associations_at_correct_sequences()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            FileAssociations = new List<FileAssociationModel>
            {
                new FileAssociationModel { Extension = ".txt", ProgId = "App.Text" },
            },
        })));

        Assert.Equal(5500, seq["RegisterExtensionInfo"]);
        Assert.Equal(3000, seq["UnregisterExtensionInfo"]);
    }

    // ── Conditional: Services ─────────────────────────────────────────────────

    [Fact]
    public void Produce_with_services_emits_all_four_service_actions()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            Services = new List<ServiceModel>
            {
                new ServiceModel { Name = "MySvc", DisplayName = "My Service", Executable = "svc.exe" },
            },
        })));

        Assert.Contains("StopServices",    names);
        Assert.Contains("DeleteServices",  names);
        Assert.Contains("InstallServices", names);
        Assert.Contains("StartServices",   names);
    }

    [Fact]
    public void Produce_with_services_service_actions_at_correct_sequences()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            Services = new List<ServiceModel>
            {
                new ServiceModel { Name = "MySvc", DisplayName = "My Service", Executable = "svc.exe" },
            },
        })));

        Assert.Equal(1900, seq["StopServices"]);
        Assert.Equal(2000, seq["DeleteServices"]);
        Assert.Equal(5800, seq["InstallServices"]);
        Assert.Equal(5900, seq["StartServices"]);
    }

    [Fact]
    public void Produce_with_only_service_controls_emits_service_actions()
    {
        // Legacy: Services.Count > 0 || ServiceControls.Count > 0 both trigger the block.
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ServiceControls = new List<ServiceControlModel>
            {
                new ServiceControlModel { Id = "sc1", ServiceName = "MySvc" },
            },
        })));

        Assert.Contains("InstallServices", names);
        Assert.Contains("StartServices",   names);
    }

    // ── Conditional: EnvironmentVariables ─────────────────────────────────────

    [Fact]
    public void Produce_with_env_vars_emits_WriteEnvironmentStrings_and_RemoveEnvironmentStrings()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            EnvironmentVariables = new List<EnvironmentVariableModel>
            {
                new EnvironmentVariableModel { Name = "PATH", Value = "%PATH%;C:\\App" },
            },
        })));

        Assert.Contains("WriteEnvironmentStrings",  names);
        Assert.Contains("RemoveEnvironmentStrings", names);
    }

    [Fact]
    public void Produce_with_env_vars_at_correct_sequences()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            EnvironmentVariables = new List<EnvironmentVariableModel>
            {
                new EnvironmentVariableModel { Name = "PATH", Value = "%PATH%;C:\\App" },
            },
        })));

        Assert.Equal(5200, seq["WriteEnvironmentStrings"]);
        Assert.Equal(3300, seq["RemoveEnvironmentStrings"]);
    }

    // ── Conditional: CreateFolders ────────────────────────────────────────────

    [Fact]
    public void Produce_with_create_folders_emits_CreateFolders_and_RemoveFolders()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            CreateFolders = new List<CreateFolderModel>
            {
                new CreateFolderModel { Id = "cf1", DirectoryRef = "INSTALLDIR" },
            },
        })));

        Assert.Contains("CreateFolders", names);
        Assert.Contains("RemoveFolders", names);
    }

    [Fact]
    public void Produce_with_create_folders_at_correct_sequences()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            CreateFolders = new List<CreateFolderModel>
            {
                new CreateFolderModel { Id = "cf1", DirectoryRef = "INSTALLDIR" },
            },
        })));

        Assert.Equal(3700, seq["CreateFolders"]);
        Assert.Equal(3600, seq["RemoveFolders"]);
    }

    // ── Conditional: MoveFiles ────────────────────────────────────────────────

    [Fact]
    public void Produce_with_move_files_emits_MoveFiles_at_3800()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            MoveFiles = new List<MoveFileModel>
            {
                new MoveFileModel
                {
                    Id = "m1", SourceDirectory = "INSTALLDIR", SourceFileName = "src.txt",
                    DestDirectory = "INSTALLDIR",
                },
            },
        })));

        Assert.Equal(3800, seq["MoveFiles"]);
    }

    // ── Conditional: DuplicateFiles ───────────────────────────────────────────

    [Fact]
    public void Produce_with_duplicate_files_emits_DuplicateFiles_and_RemoveDuplicateFiles()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            DuplicateFiles = new List<DuplicateFileModel>
            {
                new DuplicateFileModel { Id = "d1", FileRef = "file1" },
            },
        })));

        Assert.Contains("DuplicateFiles",       names);
        Assert.Contains("RemoveDuplicateFiles", names);
    }

    [Fact]
    public void Produce_with_duplicate_files_at_correct_sequences()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            DuplicateFiles = new List<DuplicateFileModel>
            {
                new DuplicateFileModel { Id = "d1", FileRef = "file1" },
            },
        })));

        Assert.Equal(4210, seq["DuplicateFiles"]);
        Assert.Equal(3180, seq["RemoveDuplicateFiles"]);
    }

    // ── Conditional: MajorUpgrade ─────────────────────────────────────────────

    [Fact]
    public void Produce_major_upgrade_after_InstallValidate_emits_RemoveExistingProducts_at_1450()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallValidate,
                MigrateFeatures = false,
            },
        })));

        Assert.Equal(1450, seq["RemoveExistingProducts"]);
    }

    [Fact]
    public void Produce_major_upgrade_after_InstallInitialize_emits_RemoveExistingProducts_at_1550()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallInitialize,
                MigrateFeatures = false,
            },
        })));

        Assert.Equal(1550, seq["RemoveExistingProducts"]);
    }

    [Fact]
    public void Produce_major_upgrade_after_InstallExecute_emits_RemoveExistingProducts_at_6500()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallExecute,
                MigrateFeatures = false,
            },
        })));

        Assert.Equal(6500, seq["RemoveExistingProducts"]);
    }

    [Fact]
    public void Produce_major_upgrade_after_InstallExecuteAgain_emits_RemoveExistingProducts_at_6550()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallExecuteAgain,
                MigrateFeatures = false,
            },
        })));

        Assert.Equal(6550, seq["RemoveExistingProducts"]);
    }

    [Fact]
    public void Produce_major_upgrade_after_InstallFinalize_emits_RemoveExistingProducts_at_6650()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallFinalize,
                MigrateFeatures = false,
            },
        })));

        Assert.Equal(6650, seq["RemoveExistingProducts"]);
    }

    [Fact]
    public void Produce_major_upgrade_with_migrate_features_emits_MigrateFeatureStates_at_1401()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallValidate,
                MigrateFeatures = true,
            },
        })));

        Assert.Equal(1401, seq["MigrateFeatureStates"]);
    }

    [Fact]
    public void Produce_major_upgrade_without_migrate_features_does_not_emit_MigrateFeatureStates()
    {
        IReadOnlyList<string> names = ActionNames(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallValidate,
                MigrateFeatures = false,
            },
        })));

        Assert.DoesNotContain("MigrateFeatureStates", names);
    }

    // ── Conditional: UpgradeModel (non-major) ─────────────────────────────────

    [Fact]
    public void Produce_with_upgrade_model_emits_RemoveExistingProducts_at_1450()
    {
        // Legacy: package.Upgrade != null → RemoveExistingProducts at fixed seq 1450.
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            Upgrade = new UpgradeModel(),
        })));

        Assert.Equal(1450, seq["RemoveExistingProducts"]);
    }

    // ── Conditional: User ExecuteSequenceActions ──────────────────────────────

    [Fact]
    public void Produce_custom_execute_action_at_explicit_number_lands_at_that_number()
    {
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "MyAction",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.AtNumber(2500),
                },
            },
        })));

        Assert.Equal(2500, seq["MyAction"]);
    }

    [Fact]
    public void Produce_custom_execute_action_after_InstallFiles_lands_at_4001()
    {
        // AfterAction("InstallFiles") → 4000 + 1 = 4001.
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "PostInstall",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.AfterAction("InstallFiles"),
                },
            },
        })));

        Assert.Equal(4001, seq["PostInstall"]);
    }

    [Fact]
    public void Produce_custom_execute_action_before_InstallFiles_lands_at_3999()
    {
        // BeforeAction("InstallFiles") → 4000 - 1 = 3999.
        Dictionary<string, int> seq = SeqByName(ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "PreInstall",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.BeforeAction("InstallFiles"),
                },
            },
        })));

        Assert.Equal(3999, seq["PreInstall"]);
    }

    [Fact]
    public void Produce_custom_execute_action_with_condition_emits_string_condition_cell()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "CondAction",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = "NOT INSTALLED",
                    Position = new ActionPosition.AtNumber(2700),
                },
            },
        }));
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "CondAction");

        CellValue.StringValue condCell = Assert.IsType<CellValue.StringValue>(row.Cells[1]);
        Assert.Equal("NOT INSTALLED", condCell.Value);
    }

    [Fact]
    public void Produce_custom_execute_action_with_null_condition_emits_null_cell()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "NullCondAction",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.AtNumber(2700),
                },
            },
        }));
        RecipeRow row = rows.First(r => ((CellValue.StringValue)r.Cells[0]).Value == "NullCondAction");

        Assert.IsType<CellValue.Null>(row.Cells[1]);
    }

    [Fact]
    public void Produce_two_custom_execute_actions_at_same_sequence_get_different_numbers()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(1, 0, 0),
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "ActionX",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.AtNumber(2500),
                },
                new SequenceActionModel
                {
                    ActionName = "ActionY",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = null,
                    Position = new ActionPosition.AtNumber(2500),
                },
            },
        }));
        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        Assert.Equal(seqs.Length, seqs.Distinct().Count());
    }

    // ── Combined fanin ────────────────────────────────────────────────────────

    [Fact]
    public void Produce_combined_all_conditional_families_emit_all_expected_rows()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            Fonts = new List<FontModel>
            {
                new FontModel { FileName = "f.ttf" },
            },
            IniFiles = new List<IniFileModel>
            {
                new IniFileModel { FileName = "app.ini", Section = "S", Key = "K", Value = "V" },
            },
            FileAssociations = new List<FileAssociationModel>
            {
                new FileAssociationModel { Extension = ".txt", ProgId = "App.Text" },
            },
            Services = new List<ServiceModel>
            {
                new ServiceModel { Name = "Svc", DisplayName = "Service", Executable = "svc.exe" },
            },
            EnvironmentVariables = new List<EnvironmentVariableModel>
            {
                new EnvironmentVariableModel { Name = "PATH", Value = "%PATH%;." },
            },
            CreateFolders = new List<CreateFolderModel>
            {
                new CreateFolderModel { Id = "cf1", DirectoryRef = "INSTALLDIR" },
            },
            MoveFiles = new List<MoveFileModel>
            {
                new MoveFileModel
                {
                    Id = "m1", SourceDirectory = "INSTALLDIR",
                    SourceFileName = "s.txt", DestDirectory = "INSTALLDIR",
                },
            },
            DuplicateFiles = new List<DuplicateFileModel>
            {
                new DuplicateFileModel { Id = "d1", FileRef = "f1" },
            },
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallValidate,
                MigrateFeatures = true,
            },
            ExecuteSequenceActions = new List<SequenceActionModel>
            {
                new SequenceActionModel
                {
                    ActionName = "CustomExec",
                    Table = SequenceTable.InstallExecuteSequence,
                    Condition = "NOT Installed",
                    Position = new ActionPosition.AtNumber(2800),
                },
            },
        }));
        IReadOnlyList<string> names = ActionNames(rows);

        // Baseline present
        Assert.Contains("InstallFiles",    names);
        Assert.Contains("InstallFinalize", names);

        // All conditional families
        Assert.Contains("RegisterFonts",           names);
        Assert.Contains("UnregisterFonts",         names);
        Assert.Contains("WriteIniValues",           names);
        Assert.Contains("RemoveIniValues",          names);
        Assert.Contains("RegisterExtensionInfo",    names);
        Assert.Contains("UnregisterExtensionInfo",  names);
        Assert.Contains("StopServices",             names);
        Assert.Contains("DeleteServices",           names);
        Assert.Contains("InstallServices",          names);
        Assert.Contains("StartServices",            names);
        Assert.Contains("WriteEnvironmentStrings",  names);
        Assert.Contains("RemoveEnvironmentStrings", names);
        Assert.Contains("CreateFolders",            names);
        Assert.Contains("RemoveFolders",            names);
        Assert.Contains("MoveFiles",                names);
        Assert.Contains("DuplicateFiles",           names);
        Assert.Contains("RemoveDuplicateFiles",     names);
        Assert.Contains("RemoveExistingProducts",   names);
        Assert.Contains("MigrateFeatureStates",     names);
        Assert.Contains("CustomExec",               names);
    }

    [Fact]
    public void Produce_combined_rows_sorted_ascending_by_sequence()
    {
        ImmutableArray<RecipeRow> rows = ProduceRows(MakeResolved(new PackageModel
        {
            Name = "P", Manufacturer = "M", Version = new Version(2, 0, 0),
            Fonts = new List<FontModel> { new FontModel { FileName = "f.ttf" } },
            Services = new List<ServiceModel>
            {
                new ServiceModel { Name = "Svc", DisplayName = "Service", Executable = "svc.exe" },
            },
            MajorUpgrade = new MajorUpgradeModel
            {
                Schedule = RemoveExistingProductsSchedule.AfterInstallValidate,
                MigrateFeatures = false,
            },
        }));
        int[] seqs = rows.Select(r => ((CellValue.IntValue)r.Cells[2]).Value).ToArray();

        Assert.Equal(seqs.OrderBy(x => x).ToArray(), seqs);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        InstallExecuteSequenceTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static IReadOnlyList<string> ActionNames(ImmutableArray<RecipeRow> rows)
        => rows.Select(r => ((CellValue.StringValue)r.Cells[0]).Value).ToList();

    private static Dictionary<string, int> SeqByName(ImmutableArray<RecipeRow> rows)
        => rows.ToDictionary(
            r => ((CellValue.StringValue)r.Cells[0]).Value,
            r => ((CellValue.IntValue)r.Cells[2]).Value);

    private static ResolvedPackage MakeResolved(PackageModel pkg)
        => new ResolvedPackage
        {
            Package = pkg,
            Components = new List<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
}
