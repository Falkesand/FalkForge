using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>LockPermissions</c> table — the legacy
/// <see cref="Tables.TableEmitter"/>'s <c>EmitPermissions</c> partitions
/// <see cref="PackageModel.Permissions"/> across two tables: SDDL-driven
/// entries land in <c>MsiLockPermissionsEx</c>, and User-driven entries
/// land in <c>LockPermissions</c>. This producer covers only the
/// <c>LockPermissions</c> half: an entry contributes a row when
/// <see cref="PermissionModel.Sddl"/> is null/empty and
/// <see cref="PermissionModel.User"/> is non-empty. Cells project to
/// (<c>LockObject</c>, <c>Table</c>, <c>Domain</c>, <c>User</c>,
/// <c>Permission</c>) with the nullable <c>Domain</c> column emitting
/// <see cref="CellValue.Null"/> when the model leaves it unset.
///
/// Note: a dedicated <c>MsiLockPermissionsExTableProducer</c> handles the
/// SDDL-driven half of the same model list and is intentionally out of
/// scope here so the two table producers partition the input cleanly.
/// </summary>
internal sealed class LockPermissionsTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>LockPermissions</c> table layout.</summary>
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
        foreach (PermissionModel perm in permissions)
        {
            // Mirror the legacy EmitPermissions partition: SDDL entries flow
            // through MsiLockPermissionsExTableProducer; only the User-driven
            // path contributes to the LockPermissions table.
            if (!string.IsNullOrEmpty(perm.Sddl))
            {
                continue;
            }

            if (string.IsNullOrEmpty(perm.User))
            {
                continue;
            }

            // Domain is a nullable string but it is in the primary key (columns 0-3).
            // The MSI PK validator rejects null PK cells. Mirror the legacy
            // TableEmitter which passes null to MsiRecord.SetString — msi.dll
            // normalises a null SetString call to an empty string in the stored row.
            // Use empty string so the recipe FK validator sees a consistent non-null
            // value and the INSERT produces the same byte representation as the legacy path.
            CellValue domainCell = new CellValue.StringValue(perm.Domain ?? string.Empty);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(perm.LockObject),
                new CellValue.StringValue(perm.Table),
                domainCell,
                new CellValue.StringValue(perm.User),
                new CellValue.IntValue(perm.Permission));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "LockObject",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Table",
                Type = ColumnType.String,
                Width = 32,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Domain",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "User",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Permission",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("LockPermissions").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0),
                new ColumnIndex(1),
                new ColumnIndex(2),
                new ColumnIndex(3)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
            // Opt-out of empty-table emission: the legacy TableEmitter only adds
            // the CREATE TABLE LockPermissions statement when at least one
            // User-driven permission entry is present. Setting EmitWhenEmpty to
            // false propagates that conditional to the recipe builder so packages
            // without permissions produce a byte-identical output to the legacy path.
            EmitWhenEmpty = false,
        };
    }
}
