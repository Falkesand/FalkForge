using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>CreateFolder</c> table. Walks
/// <see cref="PackageModel.CreateFolders"/> and emits one row per entry,
/// mirroring <see cref="Tables.TableEmitter"/>'s <c>EmitCreateFolders</c>:
/// the row is a pair of foreign keys — <c>Directory_</c> from
/// <see cref="CreateFolderModel.DirectoryRef"/> and <c>Component_</c> from
/// <see cref="CreateFolderModel.ComponentRef"/> with a fallback chain to
/// the first resolved component (or <c>"MainComponent"</c>). The composite
/// primary key on <c>(Directory_, Component_)</c> matches
/// <c>MsiTableDefinitions.CreateCreateFolderTable</c>.
/// </summary>
internal sealed class CreateFolderTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private static readonly TableId DirectoryTable = TableId.Create("Directory").Value;
    private static readonly TableId ComponentTable = TableId.Create("Component").Value;

    /// <summary>Static schema describing the <c>CreateFolder</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<CreateFolderModel> createFolders = resolved.Package.CreateFolders;

        if (createFolders.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(createFolders.Count);
        foreach (CreateFolderModel cf in createFolders)
        {
            string componentId = cf.ComponentRef ?? defaultComponentId;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.ForeignKey(DirectoryTable, cf.DirectoryRef),
                new CellValue.ForeignKey(ComponentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Directory_",
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
            });

        return new TableSchema
        {
            Name = TableId.Create("CreateFolder").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(0),
                    TargetTable = DirectoryTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = ComponentTable,
                }),
        };
    }
}
