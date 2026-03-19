namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;

public sealed class PlanningHandler : IEnginePhaseHandler
{
    private readonly Planner _planner;

    public PlanningHandler(Planner planner)
    {
        _planner = planner;
    }

    public EnginePhase Phase => EnginePhase.Planning;

    public async Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Apply user-selected install directory if set
        if (context.UserInstallDirectory is not null)
        {
            context.InstallDirectory = context.UserInstallDirectory;
        }

        // Populate feature selections from detected features where the user hasn't overridden them
        foreach (var featureState in context.DetectedFeatures)
        {
            context.FeatureSelections.TryAdd(featureState.FeatureId, featureState.IsSelected);
        }

        // Apply feature selections to variables for condition evaluation
        foreach (var (featureId, isSelected) in context.FeatureSelections)
        {
            context.Variables.Set($"Feature_{featureId}", isSelected ? "1" : "0");
        }

        // PLN004: Block dry-run if any extension does not support it
        if (context.Manifest.IsDryRun && context.Manifest.UnsupportedExtensions.Length > 0)
        {
            var names = string.Join(", ", context.Manifest.UnsupportedExtensions);
            context.ErrorMessage = $"PLN004: Dry-run mode is not supported by the following extension(s): {names}. All extensions must implement IDryRunContributor to use dry-run.";
            context.LastErrorKind = ErrorKind.DryRunNotSupported;
            context.Logger.Error("Planning", context.ErrorMessage);
            return EnginePhase.Failed;
        }

        // Block uninstall if dependencies exist (before notifying UI of plan begin)
        if (context.RequestedAction == InstallAction.Uninstall && context.DependencyBlockers.Count > 0)
        {
            var blockerNames = string.Join(", ", context.DependencyBlockers.Select(b => b.DisplayName ?? b.ProviderKey));
            context.Logger.Error("Dependency", $"Cannot uninstall: other products depend on this package ({blockerNames})");
            return EnginePhase.Failed;
        }

        // Notify UI that planning is beginning
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            await context.UiPipe.SendAsync(new PlanBeginMessage
            {
                Action = context.RequestedAction
            }, ct);
        }

        var detection = new DetectionResult(
            context.DetectedState,
            context.DetectedVersion,
            context.DetectedFeatures);

        var featureSnapshot = new Dictionary<string, bool>(context.FeatureSelections);
        var planResult = _planner.CreatePlan(
            context.Manifest,
            detection,
            context.RequestedAction,
            context.Variables,
            context.DetectedRelatedBundles,
            featureSnapshot,
            context.UserProperties,
            context.SecretPropertyNames.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (planResult.IsFailure)
        {
            context.ErrorMessage = planResult.Error.Message;
            return EnginePhase.Failed;
        }

        context.CurrentPlan = planResult.Value;

        // Notify UI that planning is complete
        if (context.UiPipe is not null && context.UiPipe.IsConnected)
        {
            var packageIds = planResult.Value.Actions
                .Select(a => a.PackageId)
                .ToArray();

            await context.UiPipe.SendAsync(new PlanCompleteMessage
            {
                TotalDiskSpaceRequired = planResult.Value.TotalDiskSpaceRequired,
                PackageIds = packageIds
            }, ct);
        }

        // In plan-only mode, export the plan and shut down without installing
        if (context.IsPlanOnly)
        {
            if (context.PlanOnlyOutputPath is not null)
            {
                var writeResult = PlanExporter.WriteToFile(planResult.Value, context.PlanOnlyOutputPath);
                if (writeResult.IsFailure)
                {
                    context.ErrorMessage = writeResult.Error.Message;
                    return EnginePhase.Failed;
                }
            }
            else
            {
                var json = PlanExporter.ToJson(planResult.Value);
                Console.WriteLine(json);
            }

            return EnginePhase.Shutdown;
        }

        // PerUser installs don't need elevation
        return context.Manifest.Scope == InstallScope.PerUser
            ? EnginePhase.Applying
            : EnginePhase.Elevating;
    }
}
