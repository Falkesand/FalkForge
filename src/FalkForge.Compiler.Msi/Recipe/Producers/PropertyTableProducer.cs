using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Property</c> table. Walks
/// <see cref="PackageModel.Properties"/> in dictionary insertion order and
/// emits one row per property whose value is non-null. The column shape and
/// row projection mirror <see cref="TableEmitter"/>'s
/// <c>InsertPropertyRow</c> helper so the recipe pipeline produces the same
/// MSI bits as the legacy emitter.
/// </summary>
internal sealed class PropertyTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Property</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        foreach (PropertyModel property in context.Resolved.Package.Properties)
        {
            // Property rows with a null value are skipped: the MSI Property table's
            // Value column is NOT NULL, and TableEmitter's InsertPropertyRow uses
            // SetString which would error on null.
            if (property.Value is null)
            {
                continue;
            }

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(property.Name),
                new CellValue.StringValue(property.Value));
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
