using FalkForge.Extensibility;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Contributes one <c>LaunchCondition</c> row per version-constrained dependency consumer.
///     <c>LaunchCondition</c> is a built-in table already produced (with
///     <c>TableSchema.EmitWhenEmpty = true</c>) by the fixed compiler pipeline, so these rows
///     merge into it rather than creating a custom table. The standard <c>LaunchConditions</c>
///     action is already scheduled early in both <c>InstallUISequence</c> (sequence 100) and
///     <c>InstallExecuteSequence</c> (sequence 100) — after <c>AppSearch</c> (50), before
///     <c>InstallInitialize</c> (1500) — so no new sequencing is required: the MSI engine
///     evaluates every row of this table for both sequences automatically, aborting with the
///     row's Description before any part of the install commits.
/// </summary>
internal sealed class DependencyLaunchConditionContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DependencyConsumerModel> _consumers;

    internal DependencyLaunchConditionContributor(IReadOnlyList<DependencyConsumerModel> consumers)
    {
        _consumers = consumers;
    }

    public string TableName => "LaunchCondition";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var plan = DependencyVersionCheckPlanner.Plan(_consumers);
        if (plan.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(plan.Count);
        foreach (var check in plan)
        {
            rows.Add(new MsiTableRow()
                .Set("Condition", check.Condition)
                .Set("Description", check.Message));
        }

        return rows;
    }
}
