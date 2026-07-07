using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>RemoveFile</c> table. Walks
/// <see cref="PackageModel.RemoveFiles"/> and projects each entry onto the
/// column shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9) <c>EmitRemoveFiles</c>.
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

        TableId componentTable = WellKnownTableIds.Component;
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

            // DirProperty accepts a Directory key or a Windows Installer property name
            // (e.g. "LOGSDIR", "INSTALLDIR") resolved at install time — not a strict
            // compile-time FK. Emit as plain string to mirror the legacy TableEmitter.EmitRemoveFiles
            // (deleted in Phase 9) which used SetString for this column.
            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(rf.Id),
                new CellValue.ForeignKey(componentTable, componentId),
                fileNameCell,
                new CellValue.StringValue(rf.DirectoryRef),
                new CellValue.IntValue(installMode));
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
            RecipeColumn.Localized("FileName", 255, nullable: true),
            RecipeColumn.String("DirProperty", 72),
            RecipeColumn.Integer("InstallMode", 2));

        return new TableSchema
        {
            Name = WellKnownTableIds.RemoveFile,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            // DirProperty (col 3) accepts Directory keys or property names resolved at install
            // time — not a compile-time FK. Only Component_ (col 1) is a strict compile-time FK.
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = componentTable,
                }),
        };
    }
}
