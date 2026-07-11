using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Property</c> table. Synthesizes the MSI built-in
/// properties (<c>ProductName</c>, <c>Manufacturer</c>, <c>ProductVersion</c>,
/// <c>ProductCode</c>, <c>UpgradeCode</c>, <c>ProductLanguage</c>,
/// <c>ALLUSERS</c>, optional <c>MSIRMSHUTDOWN</c>) from the
/// <see cref="PackageModel"/> headline fields, then layers the user-supplied
/// <see cref="PackageModel.Properties"/> on top — matching the order and
/// override semantics of the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitProperties</c> dictionary build:
/// built-ins seeded first, user properties overwrite by key, empty values
/// skipped at the end.
/// </summary>
/// <remarks>
/// Round-trip parity is exercised end-to-end by
/// <c>MsiAuthoringTests.Compile_with_simple_package_round_trips_property_table</c>,
/// which compiles a minimal package through <see cref="MsiAuthoring"/> and
/// reads the resulting MSI's Property table back via msi.dll, asserting the
/// synthesized <c>ProductName</c> and <c>Manufacturer</c> rows are present.
/// </remarks>
internal sealed class PropertyTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Property</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        // Mirror legacy TableEmitter.EmitProperties (deleted in Phase 9): keyed dictionary preserves
        // insertion order, user props overwrite built-ins by key, then a
        // final pass skips entries with null/empty values.
        Dictionary<string, string> props = new(StringComparer.Ordinal)
        {
            ["ProductName"] = package.Name,
            ["Manufacturer"] = package.Manufacturer,
            ["ProductVersion"] = package.Version.ToString(3),
            ["ProductCode"] = package.ProductCode.ToString("B").ToUpperInvariant(),
            ["UpgradeCode"] = package.UpgradeCode.ToString("B").ToUpperInvariant(),
            ["ProductLanguage"] = "1033",
            ["ALLUSERS"] = package.Scope == InstallScope.PerMachine ? "1" : string.Empty,
        };

        if (package.EnableRestartManager)
        {
            props["MSIRMSHUTDOWN"] = "2";
        }

        // ARPPRODUCTICON points Add/Remove Programs at an Icon table row. The
        // value must equal the Icon.Name that IconTableProducer emits for the
        // same source path — both derive it via ProducerHelpers.ResolveIconName,
        // so they agree without an ordering dependency.
        if (package.ProductIcon is { Length: > 0 } productIcon)
        {
            props["ARPPRODUCTICON"] = ProducerHelpers.ResolveIconName(productIcon);
        }

        foreach (PropertyModel property in package.Properties)
        {
            if (property.Value is null)
            {
                continue;
            }

            props[property.Name] = property.Value;
        }

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>(props.Count);
        foreach (KeyValuePair<string, string> entry in props)
        {
            // Legacy parity: empty (or null) values are dropped at emission time.
            // The MSI Property.Value column is NOT NULL.
            if (string.IsNullOrEmpty(entry.Value))
            {
                continue;
            }

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(entry.Key),
                new CellValue.StringValue(entry.Value));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Property", 72),
            RecipeColumn.Localized("Value", 0));

        return new TableSchema
        {
            Name = WellKnownTableIds.Property,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
