using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>MsiLockPermissionsEx</c> table — the
/// <see cref="Tables.TableEmitter"/>'s <c>EmitPermissions</c> partitions
/// <see cref="PackageModel.Permissions"/> across two MSI tables: SDDL-driven
/// entries land here, User-driven entries land in
/// <c>LockPermissionsTableProducer</c>'s output. This producer covers only
/// the <c>MsiLockPermissionsEx</c> half: an entry contributes a row when
/// <see cref="PermissionModel.Sddl"/> is non-empty. Cells project to
/// (<c>MsiLockPermissionsEx</c>, <c>LockObject</c>, <c>Table</c>,
/// <c>SDDLText</c>, <c>Condition</c>) with a synthesized
/// <c>PRM_NNNN</c> primary key whose numeric suffix matches the entry's
/// position in the input list — the legacy emitter uses a single index
/// counter and skipped User-only entries still advance it, so the producer
/// reproduces that input-index synthesis exactly to keep recipes byte-for-byte
/// compatible with the legacy MSI on hand-curated permission lists.
///
/// Note: PermissionModel has no field for the <c>Condition</c> column; the
/// legacy emitter writes a literal null and the producer pins that
/// literal so a future field addition does not silently change MSI
/// behaviour.
/// </summary>
internal sealed class MsiLockPermissionsExTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>MsiLockPermissionsEx</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<PermissionModel> permissions = context.Resolved.Package.Permissions;

        if (permissions.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        for (int index = 0; index < permissions.Count; index++)
        {
            PermissionModel perm = permissions[index];
            if (string.IsNullOrEmpty(perm.Sddl))
            {
                // User-driven entries belong to LockPermissions; their input
                // position still consumes the shared index counter so a
                // later Sddl entry keeps its legacy PRM_NNNN id.
                continue;
            }

            string permId = string.Create(
                CultureInfo.InvariantCulture,
                $"PRM_{index:D4}");

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(permId),
                new CellValue.StringValue(perm.LockObject),
                new CellValue.StringValue(perm.Table),
                new CellValue.StringValue(perm.Sddl),
                new CellValue.Null());
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("MsiLockPermissionsEx", 72),
            RecipeColumn.String("LockObject", 72),
            RecipeColumn.String("Table", 32),
            RecipeColumn.String("SDDLText", 255),
            RecipeColumn.String("Condition", 255, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.MsiLockPermissionsEx,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
            // Opt-out of empty-table emission: the legacy TableEmitter only adds
            // the CREATE TABLE MsiLockPermissionsEx statement when at least one
            // SDDL-driven permission entry is present. Setting EmitWhenEmpty to
            // false propagates that conditional to the recipe builder so packages
            // without SDDL permissions produce a byte-identical output to the legacy path.
            EmitWhenEmpty = false,
        };
    }
}
