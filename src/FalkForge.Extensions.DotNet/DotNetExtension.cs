using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

public sealed class DotNetExtension : IFalkForgeExtension, IDryRunContributor
{
    private readonly List<DotNetCoreSearchModel> _searches = [];

    public string Name => "DotNet";

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install => [new DryRunAction { Kind = DryRunActionKind.FileSystem, Description = "Would detect .NET runtime via registry and filesystem" }],
            _ => []
        };

    /// <summary>
    ///     Adds a search to be emitted as MSI-native <c>Signature</c>/<c>DrLocator</c>/<c>AppSearch</c>
    ///     detection (plus an optional <c>LaunchCondition</c> when <see cref="DotNetCoreSearchModel.Message"/>
    ///     is set) — see <see cref="DotNetSearchPlanner"/>. Re-validates the model (including duplicate
    ///     <see cref="DotNetCoreSearchModel.VariableName"/> detection across every search already added)
    ///     because a model built outside <see cref="DotNetCoreSearchBuilder"/> would otherwise bypass its
    ///     validation.
    /// </summary>
    public Result<Unit> AddSearch(DotNetCoreSearchModel model)
    {
        if (model is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "NET006: search model is required.");

        var candidate = new List<DotNetCoreSearchModel>(_searches.Count + 1);
        candidate.AddRange(_searches);
        candidate.Add(model);

        var validation = DotNetSearchValidator.ValidateAll(candidate);
        if (validation.IsFailure)
            return Result<Unit>.Failure(validation.Error);

        _searches.Add(model);
        return Unit.Value;
    }

    public void Register(IExtensionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        // MSI-native detection for every search added via AddSearch. A package that never calls
        // AddSearch keeps the extension detection-only (no tables), matching its original
        // engine-detect-phase-only behavior.
        var plans = DotNetSearchPlanner.Plan(_searches);
        if (plans.Count == 0)
            return;

        registry.RegisterTableContributor(new DotNetSignatureContributor(plans));
        registry.RegisterTableContributor(new DotNetDrLocatorContributor(plans));
        registry.RegisterTableContributor(new DotNetAppSearchContributor(plans));
        registry.RegisterTableContributor(new DotNetLaunchConditionContributor(plans));
    }

    public DotNetCoreSearchBuilder SearchForRuntime()
    {
        return new DotNetCoreSearchBuilder();
    }
}