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
    public void Produce_with_restart_manager_emits_msirmshutdown()
    {
        ResolvedPackage resolved = MakeResolved(
            properties: System.Array.Empty<PropertyModel>(),
            enableRestartManager: true);

        ImmutableArray<RecipeRow> rows = ProduceRows(resolved);

        AssertRow(rows, "MSIRMSHUTDOWN", "2");
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
            new MsiRecipeBuildOptions(),
            new NoOpFileSequencer(),
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
        bool enableRestartManager = false)
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
                Properties = properties,
            },
            Components = new List<ResolvedComponent>(),
            Files = new List<ResolvedFile>(),
        };
    }
}
