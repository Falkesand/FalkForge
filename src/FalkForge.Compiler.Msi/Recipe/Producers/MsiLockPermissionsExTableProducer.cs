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
/// Note: the <c>Condition</c> column stays a literal null for entries with no
/// <see cref="PermissionModel.FeatureRef"/>, matching the legacy emitter and keeping the
/// fallback path byte-identical. A feature-gated entry (declared via
/// <c>FeatureBuilder.Permission(...)</c>) instead encodes the standard MSI feature-state
/// condition <c>&amp;FeatureId=3</c> — the only column this table offers for gating a row's
/// execution, since <c>MsiLockPermissionsEx</c> has no <c>Component_</c>/<c>Feature_</c> column
/// of its own.
///
/// The input list is <see cref="ServicePermissionSource.EnumerateAll"/> rather
/// than <see cref="PackageModel.Permissions"/> alone: it also walks each
/// service's own <see cref="ServiceModel.Permissions"/> (fluent
/// <c>ServiceBuilder.Permission(...)</c>), which package-level-only iteration
/// silently dropped. Per-service entries use the enumeration's
/// <c>EffectiveLockObject</c> — the service's synthesized ServiceInstall
/// primary key — rather than <see cref="PermissionModel.LockObject"/>, which
/// only holds the raw service name. The <c>PRM_NNNN</c> index counter still
/// walks the combined list in package-then-service order, so existing
/// package-only packages keep their legacy numbering byte-for-byte.
/// </summary>
internal sealed class MsiLockPermissionsExTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>MsiLockPermissionsEx</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<(PermissionModel Permission, string EffectiveLockObject)> permissions = new();
        foreach ((PermissionModel Permission, string EffectiveLockObject) entry in ServicePermissionSource.EnumerateAll(context.Resolved))
        {
            permissions.Add(entry);
        }

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        for (int index = 0; index < permissions.Count; index++)
        {
            (PermissionModel perm, string effectiveLockObject) = permissions[index];
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

            // A FeatureRef (declared via FeatureBuilder.Permission(...)) is encoded as the
            // standard MSI feature-state condition "&FeatureId=3" ("feature installed locally") —
            // the only column MsiLockPermissionsEx offers for gating a row's execution, since the
            // table has no Component_/Feature_ column of its own. Ungated entries keep the
            // pre-existing literal null so the fallback path stays byte-identical.
            CellValue conditionCell = perm.FeatureRef is null
                ? new CellValue.Null()
                : new CellValue.StringValue($"&{perm.FeatureRef}=3");

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(permId),
                new CellValue.StringValue(effectiveLockObject),
                new CellValue.StringValue(perm.Table),
                new CellValue.StringValue(perm.Sddl),
                conditionCell);
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
