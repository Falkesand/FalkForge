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

public sealed class ServiceControlTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_servicecontrol_pk_component_fk()
    {
        ServiceControlTableProducer producer = new();

        Assert.Equal("ServiceControl", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("ServiceControl", producer.Schema.Columns[0].Name);
        Assert.Equal("Name", producer.Schema.Columns[1].Name);
        Assert.Equal("Event", producer.Schema.Columns[2].Name);
        Assert.Equal("Arguments", producer.Schema.Columns[3].Name);
        Assert.Equal("Wait", producer.Schema.Columns[4].Name);
        Assert.Equal("Component_", producer.Schema.Columns[5].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(5, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }

    [Fact]
    public void Schema_column_types_match_msi_table_definitions()
    {
        // MsiTableDefinitions.CreateServiceControlTable defines this table without
        // any LOCALIZABLE flag on Name or Arguments, so neither column may carry
        // ColumnType.Localized. Event and Wait are SHORT integers, ServiceControl
        // and Component_ are CHAR(72) identifier columns. Catch any drift between
        // the producer schema and the legacy DDL early — once a future phase
        // drives DDL emission from RecipeColumn, a mismatch here would silently
        // produce a non-WiX-shaped MSI.
        ServiceControlTableProducer producer = new();
        ImmutableArray<RecipeColumn> columns = producer.Schema.Columns;

        Assert.Equal(ColumnType.String, columns[0].Type);
        Assert.Equal(ColumnType.String, columns[1].Type);
        Assert.Equal(ColumnType.Integer, columns[2].Type);
        Assert.Equal(ColumnType.String, columns[3].Type);
        Assert.Equal(ColumnType.Integer, columns[4].Type);
        Assert.Equal(ColumnType.String, columns[5].Type);

        Assert.False(columns[0].Nullable);
        Assert.False(columns[1].Nullable);
        Assert.False(columns[2].Nullable);
        Assert.True(columns[3].Nullable);
        Assert.True(columns[4].Nullable);
        Assert.False(columns[5].Nullable);
    }

    [Fact]
    public void Produce_with_no_service_controls_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(
            controls: Array.Empty<ServiceControlModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_one_row_per_service_control_with_correct_cells()
    {
        ServiceControlModel control = new()
        {
            Id = "Svc.Stop",
            ServiceName = "MyService",
            Events = ServiceControlEvent.StopOnInstall | ServiceControlEvent.StopOnUninstall,
            Wait = false,
            Arguments = "/quiet",
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolved(
            controls: new[] { control },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("Svc.Stop", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("MyService", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal((int)control.Events, ((CellValue.IntValue)row.Cells[2]).Value);
        Assert.Equal("/quiet", ((CellValue.StringValue)row.Cells[3]).Value);
        Assert.Equal(0, ((CellValue.IntValue)row.Cells[4]).Value);
        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(row.Cells[5]);
        Assert.Equal("Component", fk.TargetTable.Value);
        Assert.Equal("Comp1", fk.TargetKey);
    }

    [Fact]
    public void Produce_maps_wait_true_to_one_and_null_arguments_to_null_cell()
    {
        ServiceControlModel control = new()
        {
            Id = "Svc.Start",
            ServiceName = "MyService",
            Events = ServiceControlEvent.StartOnInstall,
            Wait = true,
            Arguments = null,
        };
        ResolvedPackage resolved = MakeResolved(
            controls: new[] { control },
            components: new[] { MakeComponent("DefaultComp") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.IsType<CellValue.Null>(row.Cells[3]);
        Assert.Equal(1, ((CellValue.IntValue)row.Cells[4]).Value);
    }

    [Fact]
    public void Produce_falls_back_to_first_resolved_component_when_componentref_missing()
    {
        ServiceControlModel control = new()
        {
            Id = "Svc.A",
            ServiceName = "MyService",
            Events = ServiceControlEvent.StartOnInstall,
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            controls: new[] { control },
            components: new[]
            {
                MakeComponent("FirstComp"),
                MakeComponent("SecondComp"),
            });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[5]);
        Assert.Equal("FirstComp", fk.TargetKey);
    }

    [Fact]
    public void Produce_falls_back_to_main_component_when_no_components_resolved()
    {
        ServiceControlModel control = new()
        {
            Id = "Svc.A",
            ServiceName = "MyService",
            Events = ServiceControlEvent.StartOnInstall,
            ComponentRef = null,
        };
        ResolvedPackage resolved = MakeResolved(
            controls: new[] { control },
            components: Array.Empty<ResolvedComponent>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        CellValue.ForeignKey fk = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[5]);
        Assert.Equal("MainComponent", fk.TargetKey);
    }

    // -------------------------------------------------------------------------
    // Auto-row generation from ServiceModel (DIV-3 parity)
    // -------------------------------------------------------------------------

    [Fact]
    public void Produce_auto_emits_start_and_stop_rows_for_each_service()
    {
        // Legacy TableEmitter.EmitServices inserts SVC_{name}_Start (Event=1)
        // and SVC_{name}_Stop (Event=2) into ServiceControl for every ServiceModel.
        // The recipe ServiceControlTableProducer must mirror this behaviour.
        ResolvedPackage resolved = MakeResolvedWithServices(
            services: new[]
            {
                new ServiceModel
                {
                    Name = "MyService",
                    DisplayName = "My Service",
                    Executable = "PlugWarden.Service.exe",
                },
            },
            controls: Array.Empty<ServiceControlModel>(),
            components: new[] { MakeComponent("C_PlugWarden.Service.exe_7C2A39DB") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);

        // Start row: SVC_MyService_Start, Event=1, Wait=1
        Assert.Equal("SVC_MyService_Start", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("MyService", ((CellValue.StringValue)rows[0].Cells[1]).Value);
        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[2]).Value);
        Assert.IsType<CellValue.Null>(rows[0].Cells[3]);
        Assert.Equal(1, ((CellValue.IntValue)rows[0].Cells[4]).Value);

        // Stop row: SVC_MyService_Stop, Event=2, Wait=1
        Assert.Equal("SVC_MyService_Stop", ((CellValue.StringValue)rows[1].Cells[0]).Value);
        Assert.Equal("MyService", ((CellValue.StringValue)rows[1].Cells[1]).Value);
        Assert.Equal(2, ((CellValue.IntValue)rows[1].Cells[2]).Value);
        Assert.IsType<CellValue.Null>(rows[1].Cells[3]);
        Assert.Equal(1, ((CellValue.IntValue)rows[1].Cells[4]).Value);
    }

    [Fact]
    public void Produce_auto_rows_use_component_matching_service_executable()
    {
        // The auto-generated ServiceControl rows must use the same component FK
        // that ServiceInstallTableProducer assigns — i.e., the component whose
        // resolved file name matches the service executable basename.
        var svcComponent = MakeComponentWithFile("C_PlugWarden_7C2A39DB", "PlugWarden.Service.exe");
        var otherComponent = MakeComponent("C_Other");
        ResolvedPackage resolved = MakeResolvedWithServices(
            services: new[]
            {
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Svc",
                    Executable = "[INSTALLFOLDER]Service\\PlugWarden.Service.exe",
                },
            },
            controls: Array.Empty<ServiceControlModel>(),
            components: new[] { svcComponent, otherComponent });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        CellValue.ForeignKey fkStart = Assert.IsType<CellValue.ForeignKey>(rows[0].Cells[5]);
        CellValue.ForeignKey fkStop = Assert.IsType<CellValue.ForeignKey>(rows[1].Cells[5]);
        Assert.Equal("C_PlugWarden_7C2A39DB", fkStart.TargetKey);
        Assert.Equal("C_PlugWarden_7C2A39DB", fkStop.TargetKey);
    }

    [Fact]
    public void Produce_auto_rows_and_explicit_controls_combined()
    {
        // Both auto-rows (from Services) and explicit ServiceControlModel rows
        // should appear in the output.
        var control = new ServiceControlModel
        {
            Id = "Svc.Stop",
            ServiceName = "MySvc",
            Events = ServiceControlEvent.StopOnInstall,
            ComponentRef = "Comp1",
        };
        ResolvedPackage resolved = MakeResolvedWithServices(
            services: new[]
            {
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Svc",
                    Executable = "mysvc.exe",
                },
            },
            controls: new[] { control },
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        // 2 auto + 1 explicit = 3 total
        Assert.Equal(3, rows.Length);
        var ids = rows.Select(r => ((CellValue.StringValue)r.Cells[0]).Value).ToHashSet();
        Assert.Contains("SVC_MySvc_Start", ids);
        Assert.Contains("SVC_MySvc_Stop", ids);
        Assert.Contains("Svc.Stop", ids);
    }

    [Fact]
    public void Produce_auto_rows_sanitize_service_name_for_identifier()
    {
        // Service names with spaces/special chars must be sanitized the same
        // way TableEmitter.SanitizeId does: non-alphanumeric non-underscore
        // non-dot chars become underscores.
        ResolvedPackage resolved = MakeResolvedWithServices(
            services: new[]
            {
                new ServiceModel
                {
                    Name = "My Service 2",
                    DisplayName = "My Service 2",
                    Executable = "svc2.exe",
                },
            },
            controls: Array.Empty<ServiceControlModel>(),
            components: new[] { MakeComponent("Comp1") });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Equal(2, rows.Length);
        Assert.Equal("SVC_My_Service_2_Start", ((CellValue.StringValue)rows[0].Cells[0]).Value);
        Assert.Equal("SVC_My_Service_2_Stop", ((CellValue.StringValue)rows[1].Cells[0]).Value);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        ServiceControlTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedComponent MakeComponent(string id)
    {
        return new ResolvedComponent
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = string.Empty,
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private static ResolvedPackage MakeResolved(
        IReadOnlyList<ServiceControlModel> controls,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                ServiceControls = controls,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private static ResolvedPackage MakeResolvedWithServices(
        IReadOnlyList<ServiceModel> services,
        IReadOnlyList<ServiceControlModel> controls,
        IReadOnlyList<ResolvedComponent> components)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Services = services,
                ServiceControls = controls,
            },
            Components = components,
            Files = Array.Empty<ResolvedFile>(),
        };
    }

    private static ResolvedComponent MakeComponentWithFile(string id, string fileName)
    {
        return new ResolvedComponent
        {
            Id = id,
            Guid = Guid.NewGuid(),
            Directory = KnownFolder.ProgramFiles / "App",
            KeyPath = fileName,
            Files = new[]
            {
                new ResolvedFile
                {
                    FileName = fileName,
                    SourcePath = $"C:\\src\\{fileName}",
                    FileId = $"F_{id}",
                    FileSize = 0,
                    ComponentId = id,
                    TargetDirectory = KnownFolder.ProgramFiles / "App",
                },
            },
        };
    }
}
