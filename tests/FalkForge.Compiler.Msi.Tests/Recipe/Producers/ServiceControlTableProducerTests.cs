using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
}
