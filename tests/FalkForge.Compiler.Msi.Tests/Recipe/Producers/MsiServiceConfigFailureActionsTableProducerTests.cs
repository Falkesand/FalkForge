using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MsiServiceConfigFailureActionsTableProducerTests
{
    [Fact]
    public void Schema_has_msiserviceconfigfailureactions_pk_and_component_fk()
    {
        MsiServiceConfigFailureActionsTableProducer producer = new();

        Assert.Equal("MsiServiceConfigFailureActions", producer.Schema.Name.Value);
        Assert.Equal(9, producer.Schema.Columns.Length);
        Assert.Equal("MsiServiceConfigFailureActions", producer.Schema.Columns[0].Name);
        Assert.Equal("Name", producer.Schema.Columns[1].Name);
        Assert.Equal("Event", producer.Schema.Columns[2].Name);
        Assert.Equal("ResetPeriod", producer.Schema.Columns[3].Name);
        Assert.Equal("RebootMessage", producer.Schema.Columns[4].Name);
        Assert.Equal("Command", producer.Schema.Columns[5].Name);
        Assert.Equal("Actions", producer.Schema.Columns[6].Name);
        Assert.Equal("DelayActions", producer.Schema.Columns[7].Name);
        Assert.Equal("Component_", producer.Schema.Columns[8].Name);

        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(8, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);

        // Matches LockPermissions/MsiLockPermissionsEx: only create the table when at
        // least one service actually configures failure actions.
        Assert.False(producer.Schema.EmitWhenEmpty);
    }

    [Fact]
    public void Produce_with_no_services_returns_empty_rows()
    {
        ResolvedPackage resolved = MakeResolved(Array.Empty<ServiceModel>());

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_skips_services_without_failure_actions()
    {
        ServiceModel service = new()
        {
            Name = "PlainService",
            DisplayName = "Plain Service",
            Executable = "svc.exe",
        };
        ResolvedPackage resolved = MakeResolved(new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        Assert.Empty(rows);
    }

    [Fact]
    public void Produce_emits_row_with_correct_cells_for_service_with_failure_actions()
    {
        // C3: ServiceBuilder.FailureActions(...) collects recovery config onto
        // ServiceModel.FailureActions, but nothing previously emitted it into the MSI.
        ServiceModel service = new()
        {
            Name = "MyService",
            DisplayName = "My Service",
            Executable = "svc.exe",
            FailureActions = new ServiceFailureActionsModel
            {
                OnFirstFailure = FailureAction.Restart,
                OnSecondFailure = FailureAction.Reboot,
                OnSubsequentFailures = FailureAction.RunCommand,
                ResetPeriod = TimeSpan.FromDays(1),
                RestartDelay = TimeSpan.FromSeconds(30),
                Command = "diagnose.exe",
                RebootMessage = "Service failed repeatedly, rebooting.",
            },
        };
        ResolvedPackage resolved = MakeResolved(new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("SCF_MyService", ((CellValue.StringValue)row.Cells[0]).Value);
        Assert.Equal("MyService", ((CellValue.StringValue)row.Cells[1]).Value);
        Assert.Equal(5, ((CellValue.IntValue)row.Cells[2]).Value); // install(1) | reinstall(4)
        Assert.Equal(86400, ((CellValue.IntValue)row.Cells[3]).Value); // 1 day in seconds
        Assert.Equal("Service failed repeatedly, rebooting.", ((CellValue.StringValue)row.Cells[4]).Value);
        Assert.Equal("diagnose.exe", ((CellValue.StringValue)row.Cells[5]).Value);
        Assert.Equal("1[~]2[~]3", ((CellValue.StringValue)row.Cells[6]).Value); // Restart, Reboot, RunCommand
        Assert.Equal("30000[~]30000[~]30000", ((CellValue.StringValue)row.Cells[7]).Value);
    }

    [Fact]
    public void Produce_emits_zero_delay_for_none_tiers_regardless_of_configured_restart_delay()
    {
        ServiceModel service = new()
        {
            Name = "PartialService",
            DisplayName = "Partial Service",
            Executable = "svc.exe",
            FailureActions = new ServiceFailureActionsModel
            {
                OnFirstFailure = FailureAction.Restart,
                OnSecondFailure = FailureAction.None,
                OnSubsequentFailures = FailureAction.None,
                RestartDelay = TimeSpan.FromMinutes(1),
            },
        };
        ResolvedPackage resolved = MakeResolved(new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.Equal("1[~]0[~]0", ((CellValue.StringValue)row.Cells[6]).Value); // Restart, None, None
        Assert.Equal("60000[~]0[~]0", ((CellValue.StringValue)row.Cells[7]).Value); // only the Restart tier gets the delay
    }

    [Fact]
    public void Produce_emits_null_cells_for_unset_rebootmessage_and_command()
    {
        ServiceModel service = new()
        {
            Name = "NoMessageService",
            DisplayName = "No Message Service",
            Executable = "svc.exe",
            FailureActions = new ServiceFailureActionsModel
            {
                OnFirstFailure = FailureAction.Restart,
            },
        };
        ResolvedPackage resolved = MakeResolved(new[] { service });

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        RecipeRow row = Assert.Single(rows);
        Assert.IsType<CellValue.Null>(row.Cells[4]);
        Assert.IsType<CellValue.Null>(row.Cells[5]);
    }

    private static ImmutableArray<RecipeRow> ProduceRows(ResolvedPackage resolved)
    {
        RecipeBuildContext context = new(
            resolved,
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());
        MsiServiceConfigFailureActionsTableProducer producer = new();
        Result<ImmutableArray<RecipeRow>> result = producer.Produce(context);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<ServiceModel> services)
    {
        return new ResolvedPackage
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Services = services,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
    }
}
