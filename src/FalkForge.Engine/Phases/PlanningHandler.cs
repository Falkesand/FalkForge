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

        // Apply feature selections to variables for condition evaluation
        foreach (var (featureId, isSelected) in context.FeatureSelections)
        {
            context.Variables.Set($"Feature_{featureId}", isSelected ? "1" : "0");
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

        var planResult = _planner.CreatePlan(
            context.Manifest,
            detection,
            context.RequestedAction,
            context.Variables,
            context.DetectedRelatedBundles);
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

        // PerUser installs don't need elevation
        return context.Manifest.Scope == InstallScope.PerUser
            ? EnginePhase.Applying
            : EnginePhase.Elevating;
    }
}
