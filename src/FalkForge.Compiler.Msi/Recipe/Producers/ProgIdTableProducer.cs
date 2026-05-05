using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>ProgId</c> table. Combines two source collections:
/// <list type="number">
///   <item>
///     <description>
///       <c>FileAssociations</c> — the ProgId-table slice of the legacy
///       <see cref="Tables.TableEmitter"/>'s <c>EmitFileAssociations</c>
///       partition. Unlike the MIME and Verb branches, ProgId fires for every
///       <see cref="FileAssociationModel"/> regardless of <c>ContentType</c> or
///       <see cref="FileAssociationModel.Verbs"/>. Cells project as
///       (<c>ProgId</c>, <c>ProgId_Parent</c>=null, <c>Class_</c>=null,
///       <c>Description</c>, <c>Icon_</c>=null, <c>IconIndex</c>) to match the
///       legacy <see cref="MsiRecord"/> writes which pin three columns to the
///       literal null because <see cref="FileAssociationModel"/> has no field
///       for them.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>ComClasses</c> — mirrors the ProgId-row emission inside the legacy
///       <see cref="Tables.TableEmitter"/>'s <c>EmitComClasses</c> loop. One row
///       is emitted per <see cref="ComClassModel"/> whose
///       <see cref="ComClassModel.ProgId"/> is non-empty. Cells project as
///       (<c>ProgId</c>, <c>ProgId_Parent</c>=null,
///       <c>Class_</c>=CLSID in braces uppercase,
///       <c>Description</c>, <c>Icon_</c>=null, <c>IconIndex</c>=0). This
///       ensures <c>Class.ProgId_Default</c> FK values resolve to rows present
///       in the <c>ProgId</c> table at runtime, closing the dangling-FK gap
///       that arises when <see cref="ClassTableProducer"/> emits the Class row
///       but no corresponding ProgId row exists.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// De-duplication policy: <b>FileAssociations source wins</b>. If the same
/// ProgId string appears in both sources the FileAssociation row is retained
/// and the ComClass row is silently skipped. This is deterministic
/// first-source-wins behaviour; no exception is thrown.
/// </para>
///
/// Note: dedicated producers for <c>Extension</c>, <c>MIME</c>, and
/// <c>Verb</c> handle the other three tables in the
/// <c>EmitFileAssociations</c> partition and are intentionally out of
/// scope here so the producer set partitions the input list cleanly.
/// </summary>
internal sealed class ProgIdTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>ProgId</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<FileAssociationModel> associations =
            context.Resolved.Package.FileAssociations;
        IReadOnlyList<ComClassModel> comClasses =
            context.Resolved.Package.ComClasses;

        if (associations.Count == 0 && comClasses.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        // Pre-size to the theoretical maximum (both sources fully populated with ProgIds).
        // The builder may hold fewer entries after de-duplication.
        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(associations.Count + comClasses.Count);

        // Track emitted ProgId PKs for de-duplication; FileAssociations wins on collision.
        // Use HashSet instead of list scan: O(1) lookup vs O(n) — relevant when both
        // sources are large.
        HashSet<string> emittedProgIds = new(associations.Count + comClasses.Count, StringComparer.Ordinal);

        // --- Source 1: FileAssociations (legacy EmitFileAssociations partition) ---
        foreach (FileAssociationModel assoc in associations)
        {
            CellValue descriptionCell = assoc.Description is null
                ? new CellValue.Null()
                : new CellValue.StringValue(assoc.Description);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(assoc.ProgId),
                new CellValue.Null(),
                new CellValue.Null(),
                descriptionCell,
                new CellValue.Null(),
                new CellValue.IntValue(assoc.IconIndex));
            rows.Add(new RecipeRow { Cells = cells });
            emittedProgIds.Add(assoc.ProgId);
        }

        // --- Source 2: ComClasses (legacy EmitComClasses ProgId sub-loop) ---
        // Mirrors: SetString(1, cls.ProgId), SetString(2, null), SetString(3, clsid),
        //          SetString(4, cls.Description), SetString(5, null), SetInteger(6, 0).
        foreach (ComClassModel cls in comClasses)
        {
            if (string.IsNullOrEmpty(cls.ProgId))
            {
                continue; // Legacy skips classes with no ProgId
            }

            if (!emittedProgIds.Add(cls.ProgId))
            {
                continue; // Collision: FileAssociation row already emitted — skip COM row
            }

            string clsid = cls.ClassId.ToString("B").ToUpperInvariant();

            CellValue descriptionCell = cls.Description is null
                ? new CellValue.Null()
                : new CellValue.StringValue(cls.Description);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(cls.ProgId),
                new CellValue.Null(),                      // ProgId_Parent: always null per legacy
                new CellValue.StringValue(clsid),          // Class_: FK to Class table
                descriptionCell,                            // Description
                new CellValue.Null(),                      // Icon_: ComClassModel has no Icon field
                new CellValue.IntValue(0));                // IconIndex: always 0 per legacy emitter
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "ProgId",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ProgId_Parent",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Class_",
                Type = ColumnType.String,
                Width = 38,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Icon_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "IconIndex",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("ProgId").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
