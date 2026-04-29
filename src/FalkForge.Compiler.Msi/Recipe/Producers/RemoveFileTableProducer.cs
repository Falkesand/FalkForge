using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>RemoveFile</c> table. Walks
/// <see cref="PackageModel.RemoveFiles"/> and projects each entry onto the
/// column shape used by <see cref="TableEmitter"/>'s <c>EmitRemoveFiles</c>.
/// <see cref="RemoveFileModel.OnInstall"/> and <see cref="RemoveFileModel.OnUninstall"/>
/// are packed into the MSI <c>InstallMode</c> bitmask (1 = install, 2 = uninstall).
/// ComponentRef falls back to the first resolved component (or
/// <c>"MainComponent"</c>) when the model omits an explicit reference.
/// </summary>
internal sealed class RemoveFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>RemoveFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = TableId.Create("Component").Value;
        TableId directoryTable = TableId.Create("Directory").Value;
        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<RemoveFileModel> removeFiles = resolved.Package.RemoveFiles;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        if (removeFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        foreach (RemoveFileModel rf in removeFiles)
        {
            int installMode = (rf.OnInstall ? 1 : 0) | (rf.OnUninstall ? 2 : 0);
            string componentId = rf.ComponentRef ?? defaultComponentId;

            CellValue fileNameCell = rf.FileName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(rf.FileName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(rf.Id),
                new CellValue.ForeignKey(componentTable, componentId),
                fileNameCell,
                new CellValue.ForeignKey(directoryTable, rf.DirectoryRef),
                new CellValue.IntValue(installMode));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = TableId.Create("Component").Value;
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "FileKey",
                Type = ColumnType.String,
                Width = 72,
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
            },
            new RecipeColumn
            {
                Name = "FileName",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DirProperty",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "InstallMode",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("RemoveFile").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = componentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(3),
                    TargetTable = directoryTable,
                }),
        };
    }
}
