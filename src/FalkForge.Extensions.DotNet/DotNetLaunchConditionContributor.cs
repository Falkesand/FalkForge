using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Contributes <c>LaunchCondition</c> rows ONLY for searches that carry a
///     <see cref="DotNetCoreSearchModel.Message"/> — the JSON authoring path, which has no separate
///     <c>package.Require(...)</c> call to gate on the detected property. The C# fluent authoring path
///     (see the <c>32-ext-dotnet</c> demo) leaves <c>Message</c> null and gates via
///     <c>PackageBuilder.Require</c> instead, so this contributor emits nothing for it — a search with
///     both a <see cref="DotNetCoreSearchModel.Message"/> AND an author-added <c>Require</c> on the same
///     property would otherwise double-emit two <c>LaunchCondition</c> rows sharing the same primary key
///     (<c>Condition</c>), which the built-in producer pipeline does not deduplicate.
///     <para>
///     <c>LaunchCondition</c> IS a built-in table (<see cref="Compiler.Msi.Recipe.Producers.LaunchConditionTableProducer"/>,
///     <c>EmitWhenEmpty=true</c>), so these rows always take the built-in-table MERGE path in
///     <c>ExtensionTableEmitter</c> — never the custom-table create path. <see cref="WriteColumns"/> is
///     declared anyway (schema-matching the built-in table) purely as a defensive fallback in case that
///     invariant ever changes; it is ignored whenever the merge path fires.
///     </para>
/// </summary>
internal sealed class DotNetLaunchConditionContributor : IMsiTableContributor
{
    private readonly IReadOnlyList<DotNetSearchPlan> _plans;

    internal DotNetLaunchConditionContributor(IReadOnlyList<DotNetSearchPlan> plans)
    {
        _plans = plans;
    }

    public string TableName => "LaunchCondition";

    public IReadOnlyList<ContributedColumn>? WriteColumns { get; } =
    [
        ContributedColumn.Key("Condition", 255),
        ContributedColumn.Text("Description", 255),
    ];

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var withMessage = _plans.Where(p => p.Message is not null).ToList();
        if (withMessage.Count == 0)
            return [];

        var rows = new List<MsiTableRow>(withMessage.Count);
        foreach (var plan in withMessage)
        {
            rows.Add(new MsiTableRow()
                .Set("Condition", plan.PropertyName)
                .Set("Description", plan.Message));
        }

        return rows;
    }
}
