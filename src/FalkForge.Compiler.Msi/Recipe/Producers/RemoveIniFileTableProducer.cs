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
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("RemoveIniFile", 72),
            RecipeColumn.Localized("FileName", 255),
            RecipeColumn.String("DirProperty", 72, nullable: true),
            RecipeColumn.Localized("Section", 96),
            RecipeColumn.Localized("Key", 128),
            RecipeColumn.Localized("Value", 255, nullable: true),
            RecipeColumn.Integer("Action", 2),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.RemoveIniFile,
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
