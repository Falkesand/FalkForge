using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>RemoveIniFile</c> table. The legacy
/// <see cref="Tables.TableEmitter"/> always issues the
/// <c>CREATE TABLE RemoveIniFile</c> statement (unconditionally, alongside
/// <c>IniFile</c>), but never populates it: <see cref="FalkForge.Models.PackageModel"/>
/// has no <c>RemoveIniFiles</c> collection. This producer mirrors that
/// behaviour — it always succeeds with an empty row set so the recipe table
/// is present and its <c>CREATE TABLE</c> SQL is issued, matching the legacy
/// byte-level output exactly.
/// </summary>
internal sealed class RemoveIniFileTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>RemoveIniFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // PackageModel has no RemoveIniFiles collection; the legacy emitter
        // creates this table unconditionally but never inserts any rows.
        return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
    }

    private static TableSchema BuildSchema()
    {
        // Schema mirrors MsiTableDefinitions.CreateRemoveIniFileTable which is
        // column-for-column identical to IniFile except the PK column is named
        // "RemoveIniFile". All column types, widths, and nullability flags are
        // copied from the DDL string.
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "RemoveIniFile",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "FileName",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DirProperty",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Section",
                Type = ColumnType.Localized,
                Width = 96,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Key",
                Type = ColumnType.Localized,
                Width = 128,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Value",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Action",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("RemoveIniFile").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(7),
                TargetTable = componentTable,
            }),
            // Always emit the table even though the row set is always empty —
            // parity with legacy TableEmitter which unconditionally issues the
            // CREATE TABLE RemoveIniFile statement. EmitWhenEmpty defaults true.
        };
    }
}
