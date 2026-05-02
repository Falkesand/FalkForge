using System.Collections.Generic;
using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Font</c> table. Walks
/// <see cref="PackageModel.Fonts"/> and emits one row per registered font,
/// mirroring <see cref="Tables.TableEmitter"/>'s <c>EmitFonts</c>: each font
/// is matched to the resolved file id by file name (case-insensitive, per
/// the legacy <see cref="System.StringComparer.OrdinalIgnoreCase"/> lookup),
/// fonts whose source file was not pulled into the resolved file set are
/// silently dropped, and <see cref="FontModel.FontTitle"/> projects to a
/// nullable cell.
/// </summary>
internal sealed class FontTableProducer : ITableProducer
{
    private static readonly TableId FileTable = TableId.Create("File").Value;

    /// <summary>Static schema describing the <c>Font</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<FontModel> fonts = resolved.Package.Fonts;

        if (fonts.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        // ResolvedFile.FileName uniqueness (case-insensitive) is an upstream
        // invariant guaranteed by the resolver; the legacy EmitFonts path
        // built the same lookup via ToDictionary which would have thrown on
        // duplicates. The producer trusts the invariant rather than
        // re-validating it here, so a hypothetical duplicate would silently
        // last-write-win instead of failing the recipe build.
        Dictionary<string, string> fileNameToFileId =
            new(resolved.Files.Count, StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedFile file in resolved.Files)
        {
            fileNameToFileId[file.FileName] = file.FileId;
        }

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>(fonts.Count);
        foreach (FontModel font in fonts)
        {
            if (!fileNameToFileId.TryGetValue(font.FileName, out string? fileId))
            {
                continue;
            }

            CellValue titleCell = font.FontTitle is null
                ? new CellValue.Null()
                : new CellValue.StringValue(font.FontTitle);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.ForeignKey(FileTable, fileId),
                titleCell);
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "File_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "FontTitle",
                Type = ColumnType.String,
                Width = 128,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Font").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(0),
                TargetTable = FileTable,
            }),
        };
    }
}
