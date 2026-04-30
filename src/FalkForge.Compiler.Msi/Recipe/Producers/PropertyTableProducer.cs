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
/// override semantics of the legacy
/// <see cref="TableEmitter"/>.<c>EmitProperties</c> dictionary build:
/// built-ins seeded first, user properties overwrite by key, empty values
/// skipped at the end.
/// </summary>
internal sealed class PropertyTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Property</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        // Mirror TableEmitter.EmitProperties: keyed dictionary preserves
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
            new RecipeColumn
            {
                Name = "Property",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Value",
                Type = ColumnType.Localized,
                Width = 0,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Property").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
