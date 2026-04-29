using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Upgrade</c> table (upgrade detection rows). Mirrors
/// <see cref="TableEmitter"/>'s <c>EmitUpgrade</c>: when
/// <see cref="PackageModel.Upgrade"/> is set, emits the <c>OLDERVERSIONFOUND</c>
/// detection row and — unless <see cref="UpgradeModel.AllowDowngrades"/> is
/// true — also the <c>NEWERVERSIONFOUND</c> row. The composite primary key on
/// <c>(UpgradeCode, VersionMin, VersionMax, Language, Attributes)</c> matches
/// <c>MsiTableDefinitions.CreateUpgradeTable</c>.
///
/// Note: <see cref="PackageModel.MajorUpgrade"/> rows are deliberately out of
/// scope for this producer batch. <c>EmitMajorUpgrade</c> shares the same
/// table but uses different attribute math; a dedicated
/// <c>MajorUpgradeTableProducer</c> can be wired in a later phase to emit
/// into the same <c>Upgrade</c> table without duplicating logic here.
/// </summary>
internal sealed class UpgradeTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Upgrade</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        PackageModel package = context.Resolved.Package;
        if (package.Upgrade is null)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        string upgradeCode = package.UpgradeCode.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
        string versionStr = package.Version.ToString(3);

        // Older-version detection row.
        rows.Add(new RecipeRow
        {
            Cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(upgradeCode),
                new CellValue.StringValue("0.0.0"),
                new CellValue.StringValue(versionStr),
                new CellValue.StringValue(string.Empty),
                new CellValue.IntValue(256),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue("OLDERVERSIONFOUND")),
        });

        if (!package.Upgrade.AllowDowngrades)
        {
            rows.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(upgradeCode),
                    new CellValue.StringValue(versionStr),
                    new CellValue.StringValue(string.Empty),
                    new CellValue.StringValue(string.Empty),
                    new CellValue.IntValue(258),
                    new CellValue.StringValue(string.Empty),
                    new CellValue.StringValue("NEWERVERSIONFOUND")),
            });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "UpgradeCode",
                Type = ColumnType.String,
                Width = 38,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "VersionMin",
                Type = ColumnType.String,
                Width = 20,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "VersionMax",
                Type = ColumnType.String,
                Width = 20,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Language",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Attributes",
                Type = ColumnType.Integer,
                Width = 4,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Remove",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ActionProperty",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Upgrade").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0),
                new ColumnIndex(1),
                new ColumnIndex(2),
                new ColumnIndex(3),
                new ColumnIndex(4)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
