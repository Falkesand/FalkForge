using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>RemoveIniFile</c> table. Walks
/// <see cref="PackageModel.RemoveIniFiles"/> and emits one row per entry, mirroring the shape
/// used by <see cref="RemoveRegistryTableProducer"/> and <see cref="RemoveFileTableProducer"/>:
/// each row is keyed on the author-supplied <see cref="RemoveIniFileModel.Id"/>, and the
/// <see cref="IniFileAction"/> enum projects to its underlying integer. ComponentRef falls back
/// to the first resolved component (or <c>"MainComponent"</c>) when the model omits an
/// explicit reference.
/// </summary>
internal sealed class RemoveIniFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private static readonly TableId ComponentTable = WellKnownTableIds.Component;

    /// <summary>Static schema describing the <c>RemoveIniFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<RemoveIniFileModel> removeIniFiles = resolved.Package.RemoveIniFiles;

        if (removeIniFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>(removeIniFiles.Count);
        foreach (RemoveIniFileModel entry in removeIniFiles)
        {
            string componentId = entry.ComponentRef ?? defaultComponentId;

            CellValue dirCell = entry.DirProperty is null
                ? new CellValue.Null()
                : new CellValue.StringValue(entry.DirProperty);

            CellValue valueCell = entry.Value is null
                ? new CellValue.Null()
                : new CellValue.StringValue(entry.Value);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(entry.Id),
                new CellValue.StringValue(entry.FileName),
                dirCell,
                new CellValue.StringValue(entry.Section),
                new CellValue.StringValue(entry.Key),
                valueCell,
                new CellValue.IntValue((int)entry.Action),
                new CellValue.ForeignKey(ComponentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
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
            // Always emit the table even when the row set is empty — parity with legacy
            // TableEmitter which unconditionally issues the CREATE TABLE RemoveIniFile
            // statement. EmitWhenEmpty defaults true.
        };
    }
}
