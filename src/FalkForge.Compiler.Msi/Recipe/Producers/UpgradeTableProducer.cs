using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Upgrade</c> table (upgrade detection rows). Mirrors
/// the legacy <c>TableEmitter</c> (deleted in Phase 9) <c>EmitUpgrade</c> and
/// <c>EmitMajorUpgrade</c>:
/// <list type="bullet">
/// <item>When <see cref="PackageModel.Upgrade"/> is set, emit the
/// <c>OLDERVERSIONFOUND</c> detection row and — unless
/// <see cref="UpgradeModel.AllowDowngrades"/> is true — also the
/// <c>NEWERVERSIONFOUND</c> row.</item>
/// <item>When <see cref="PackageModel.Upgrade"/> is null but
/// <see cref="PackageModel.MajorUpgrade"/> is set, emit a major-upgrade
/// <c>OLDERVERSIONFOUND</c> row whose attribute mask is 256 (or 768 when
/// <see cref="MajorUpgradeModel.AllowSameVersionUpgrades"/> opens the upper
/// bound) and — unless <see cref="PackageModel.Downgrade"/> is configured to
/// allow downgrades — append a <c>NEWERVERSIONFOUND</c> row with attribute 2
/// (msidbUpgradeAttributesOnlyDetect).</item>
/// </list>
/// The composite primary key on
/// <c>(UpgradeCode, VersionMin, VersionMax, Language, Attributes)</c> matches
/// <c>MsiTableDefinitions.CreateUpgradeTable</c>.
///
/// Defense in depth: when both <see cref="PackageModel.Upgrade"/> and
/// <see cref="PackageModel.MajorUpgrade"/> are configured the legacy emitter
/// silently drops the <c>MajorUpgrade</c> rows so the resulting MSI is the
/// pure <c>Upgrade</c> table; the producer reproduces that exact behaviour so
/// validators that allow the (mis-)configuration through still see the same
/// table on disk.
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

        if (package.Upgrade is null && package.MajorUpgrade is null)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        string upgradeCode = package.UpgradeCode.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
        string versionStr = package.Version.ToString(3);

        if (package.Upgrade is not null)
        {
            // Older-version detection row.
            rows.Add(MakeRow(upgradeCode, "0.0.0", versionStr, 256, "OLDERVERSIONFOUND"));

            if (!package.Upgrade.AllowDowngrades)
            {
                rows.Add(MakeRow(upgradeCode, versionStr, string.Empty, 258, "NEWERVERSIONFOUND"));
            }

            // Defense in depth: even when MajorUpgrade is also set, the legacy
            // EmitMajorUpgrade silently bails out so the Upgrade table is the
            // sole source of detection rows. Mirror that early return.
            return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
        }

        // Upgrade is null and MajorUpgrade is set — reproduce EmitMajorUpgrade.
        // Attribute bits: 256 (msidbUpgradeAttributesVersionMinInclusive)
        // matches >= 0.0.0 AND < currentVersion. AllowSameVersionUpgrades adds
        // 512 (msidbUpgradeAttributesVersionMaxInclusive) so the comparison
        // becomes <= currentVersion, allowing same-version reinstall.
        MajorUpgradeModel majorUpgrade = package.MajorUpgrade!;
        int olderAttributes = majorUpgrade.AllowSameVersionUpgrades ? 256 | 512 : 256;
        rows.Add(MakeRow(upgradeCode, "0.0.0", versionStr, olderAttributes, "OLDERVERSIONFOUND"));

        if (package.Downgrade is not { AllowDowngrades: true })
        {
            // Newer-version detection only — attribute 2
            // (msidbUpgradeAttributesOnlyDetect) — paired with a launch
            // condition emitted by the LaunchCondition producer.
            rows.Add(MakeRow(upgradeCode, versionStr, string.Empty, 2, "NEWERVERSIONFOUND"));
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static RecipeRow MakeRow(
        string upgradeCode,
        string versionMin,
        string versionMax,
        int attributes,
        string actionProperty)
    {
        return new RecipeRow
        {
            Cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(upgradeCode),
                new CellValue.StringValue(versionMin),
                new CellValue.StringValue(versionMax),
                new CellValue.StringValue(string.Empty),
                new CellValue.IntValue(attributes),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue(actionProperty)),
        };
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
