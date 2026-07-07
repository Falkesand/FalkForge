using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MoveFile</c> table. Walks
/// <see cref="PackageModel.MoveFiles"/> and projects each entry onto the
/// column shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9) <c>EmitMoveFiles</c>.
/// SourceFolder and DestFolder are emitted as foreign keys into the
/// <c>Directory</c> table so future cross-producer FK validation can verify
/// both endpoints. ComponentRef falls back to the first resolved component
/// (or <c>"MainComponent"</c>) when the model omits an explicit reference.
/// </summary>
internal sealed class MoveFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>MoveFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<MoveFileModel> moveFiles = resolved.Package.MoveFiles;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        if (moveFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        foreach (MoveFileModel mf in moveFiles)
        {
            string componentId = mf.ComponentRef ?? defaultComponentId;

            // SourceFolder and DestFolder in the MoveFile table accept either a Directory
            // table key or a Windows Installer property name (e.g. "INSTALLDIR", "LogFolder").
            // They are NOT strict FK references at compile time — MSI resolves them at
            // install time. Emit as plain strings to mirror the legacy TableEmitter.EmitMoveFiles
            // (deleted in Phase 9) which used SetString (no FK lookup) for these columns.
            CellValue sourceFolderCell = mf.SourceDirectory is null
                ? new CellValue.Null()
                : new CellValue.StringValue(mf.SourceDirectory);
            CellValue destNameCell = mf.DestFileName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(mf.DestFileName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(mf.Id),
                new CellValue.ForeignKey(componentTable, componentId),
                new CellValue.StringValue(mf.SourceFileName),
                sourceFolderCell,
                destNameCell,
                new CellValue.StringValue(mf.DestDirectory),
                new CellValue.IntValue(mf.Options));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("FileKey", 72),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.Localized("SourceName", 255, nullable: true),
            RecipeColumn.String("SourceFolder", 72, nullable: true),
            RecipeColumn.Localized("DestName", 255, nullable: true),
            RecipeColumn.String("DestFolder", 72),
            RecipeColumn.Integer("Options", 2));

        return new TableSchema
        {
            Name = WellKnownTableIds.MoveFile,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            // SourceFolder (col 3) and DestFolder (col 5) are MSI property/directory references
            // resolved at install time — not compile-time FK targets. Only Component_ (col 1)
            // is a strict compile-time FK. Mirrors legacy TableEmitter's SetString approach.
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = componentTable,
                }),
        };
    }
}
