using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>LaunchCondition</c> table. Mirrors
/// <see cref="Tables.TableEmitter"/>'s <c>EmitLaunchConditions</c>:
/// the producer first injects implicit <c>NOT NEWERVERSIONFOUND</c>
/// downgrade guards driven by <see cref="PackageModel.Upgrade"/> and
/// <see cref="PackageModel.MajorUpgrade"/>, then iterates the explicit
/// <see cref="PackageModel.LaunchConditions"/> entries. The legacy emitter
/// happily emits both downgrade guards even though they share the same
/// condition string, leaving the resulting primary-key collision for a
/// downstream validator to catch — the producer preserves that exact
/// behavior so any pre-existing recipe is reproduced bit-for-bit.
/// </summary>
internal sealed class LaunchConditionTableProducer : ITableProducer
{
    private const string DowngradeCondition = "NOT NEWERVERSIONFOUND";
    private const string DefaultDowngradeMessage = "A newer version is already installed.";

    /// <summary>Static schema describing the <c>LaunchCondition</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;
        UpgradeModel? upgrade = package.Upgrade;
        MajorUpgradeModel? majorUpgrade = package.MajorUpgrade;
        DowngradeModel? downgrade = package.Downgrade;
        IReadOnlyList<LaunchConditionModel> conditions = package.LaunchConditions;

        bool emitUpgradeGuard = upgrade is not null && !upgrade.AllowDowngrades;
        bool emitMajorUpgradeGuard =
            majorUpgrade is not null && downgrade is not { AllowDowngrades: true };

        if (!emitUpgradeGuard && !emitMajorUpgradeGuard && conditions.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        int capacity = conditions.Count
            + (emitUpgradeGuard ? 1 : 0)
            + (emitMajorUpgradeGuard ? 1 : 0);
        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(capacity);

        if (emitUpgradeGuard)
        {
            string message = upgrade!.DowngradeErrorMessage ?? DefaultDowngradeMessage;
            rows.Add(MakeRow(DowngradeCondition, message));
        }

        if (emitMajorUpgradeGuard)
        {
            string message = downgrade?.ErrorMessage ?? DefaultDowngradeMessage;
            rows.Add(MakeRow(DowngradeCondition, message));
        }

        foreach (LaunchConditionModel condition in conditions)
        {
            rows.Add(MakeRow(condition.Condition, condition.Message));
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static RecipeRow MakeRow(string condition, string description)
    {
        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.StringValue(condition),
            new CellValue.StringValue(description));
        return new RecipeRow { Cells = cells };
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Condition",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("LaunchCondition").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
