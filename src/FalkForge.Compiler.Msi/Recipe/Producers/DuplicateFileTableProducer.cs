using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>DuplicateFile</c> table. Walks
/// <see cref="PackageModel.DuplicateFiles"/> and emits one row per entry,
/// mirroring <see cref="Tables.TableEmitter"/>'s <c>EmitDuplicateFiles</c>:
/// each row carries the explicit <see cref="DuplicateFileModel.Id"/> as
/// the <c>FileKey</c> primary key, foreign keys to <c>Component</c> (with
/// the standard <c>ComponentRef</c> ?? first-resolved-component ??
/// <c>"MainComponent"</c> fallback chain) and <c>File</c>, and nullable
/// <c>DestFolder</c> / <c>DestName</c> string cells projecting from the
/// optional <see cref="DuplicateFileModel.DestDirectory"/> and
/// <see cref="DuplicateFileModel.DestFileName"/>.
/// </summary>
internal sealed class DuplicateFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private static readonly TableId ComponentTable = WellKnownTableIds.Component;
    private static readonly TableId FileTable = WellKnownTableIds.File;

    /// <summary>Static schema describing the <c>DuplicateFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<DuplicateFileModel> duplicateFiles = resolved.Package.DuplicateFiles;

        if (duplicateFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        // Build a filename → FileId lookup table so DuplicateFileModel.FileRef (a bare filename
        // like "app.exe") can be resolved to the computed MSI FileId (e.g. "F_app.exe_7C2A39DB").
        // This mirrors how legacy TableEmitter resolves the reference at insert time — it passes
        // the user-supplied FileRef directly, but the model's FileRef MUST match a resolved FileId.
        // The recipe uses CellValue.ForeignKey so the FK validator can verify the reference exists.
        Dictionary<string, string> fileNameToId = new(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedFile rf in resolved.Files)
        {
            // Prefer FileId (the MSI File table key). Fall back to FileName so that tests
            // using bare names still match when the FileId hasn't been computed yet.
            fileNameToId.TryAdd(rf.FileId, rf.FileId);
            fileNameToId.TryAdd(rf.FileName, rf.FileId);
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(duplicateFiles.Count);
        foreach (DuplicateFileModel df in duplicateFiles)
        {
            string componentId = df.ComponentRef ?? defaultComponentId;

            // Resolve FileRef to actual FileId; fall back to the raw ref if not found
            // (allows referencing file IDs that were pre-assigned by the caller).
            string fileId = fileNameToId.GetValueOrDefault(df.FileRef) ?? df.FileRef;

            // DestFolder accepts a Directory key or property name (resolved at install time).
            // Emit as plain string, matching legacy SetString behaviour.
            CellValue destFolderCell = df.DestDirectory is null
                ? new CellValue.Null()
                : new CellValue.StringValue(df.DestDirectory);
            CellValue destNameCell = df.DestFileName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(df.DestFileName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(df.Id),
                new CellValue.ForeignKey(ComponentTable, componentId),
                new CellValue.ForeignKey(FileTable, fileId),
                destFolderCell,
                destNameCell);
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("FileKey", 72),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.String("File_", 72),
            RecipeColumn.String("DestFolder", 72, nullable: true),
            RecipeColumn.Localized("DestName", 255, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.DuplicateFile,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = ComponentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(2),
                    TargetTable = FileTable,
                }),
        };
    }
}
