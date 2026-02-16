namespace FalkForge.Engine.Planning;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;

public sealed class Planner
{
    public Result<InstallPlan> CreatePlan(
        InstallerManifest manifest,
        DetectionResult detection,
        InstallAction action,
        VariableStore? variables = null,
        IReadOnlyList<RelatedBundleInfo>? detectedRelatedBundles = null)
    {
        var actions = new List<PlanAction>();

        switch (action)
        {
            case InstallAction.Install:
            {
                // Plan uninstall of upgrade-related bundles before installing new packages
                AddRelatedBundleUninstalls(detectedRelatedBundles, actions);

                var result = AddPackagesForward(manifest.Packages, PlanActionType.Install, variables, actions);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Uninstall:
            {
                var result = AddPackagesReverse(manifest.Packages, PlanActionType.Uninstall, variables, actions);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Repair:
            {
                var result = AddPackagesForward(manifest.Packages, PlanActionType.Repair, variables, actions);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Modify:
            {
                // For modify, install/uninstall based on feature selection (simplified for now)
                var result = AddPackagesForward(manifest.Packages, PlanActionType.Install, variables, actions);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            default:
                return Result<InstallPlan>.Failure(ErrorKind.PlanningError, $"Unknown action: {action}");
        }

        var segments = BuildSegments(manifest.Chain, actions);

        return new InstallPlan
        {
            Actions = actions,
            Segments = segments,
            TotalDiskSpaceRequired = 0 // Would calculate from package sizes
        };
    }

    private static List<RollbackSegment> BuildSegments(
        ManifestChainItem[] chainItems,
        List<PlanAction> allActions)
    {
        // If no chain items with boundaries, put all actions in a single default segment
        var hasBoundaries = false;
        foreach (var item in chainItems)
        {
            if (item is RollbackBoundaryManifestChainItem)
            {
                hasBoundaries = true;
                break;
            }
        }

        if (!hasBoundaries)
        {
            var defaultSegment = new RollbackSegment
            {
                BoundaryId = "__default__",
                Vital = true
            };
            defaultSegment.Actions.AddRange(allActions);
            return [defaultSegment];
        }

        // Build an action lookup by package ID for segment assignment
        var actionsByPackageId = new Dictionary<string, PlanAction>();
        foreach (var action in allActions)
        {
            actionsByPackageId[action.PackageId] = action;
        }

        // Walk chain items to build segments
        var segments = new List<RollbackSegment>();
        RollbackSegment? currentSegment = null;

        foreach (var chainItem in chainItems)
        {
            switch (chainItem)
            {
                case RollbackBoundaryManifestChainItem boundary:
                    currentSegment = new RollbackSegment
                    {
                        BoundaryId = boundary.Boundary.Id,
                        Vital = boundary.Boundary.Vital
                    };
                    segments.Add(currentSegment);
                    break;

                case PackageManifestChainItem package:
                    // If no boundary encountered yet, create a default leading segment
                    if (currentSegment is null)
                    {
                        currentSegment = new RollbackSegment
                        {
                            BoundaryId = "__default__",
                            Vital = true
                        };
                        segments.Add(currentSegment);
                    }

                    if (actionsByPackageId.TryGetValue(package.Package.Id, out var planAction))
                    {
                        currentSegment.Actions.Add(planAction);
                    }
                    break;
            }
        }

        return segments;
    }

    private static Result<Unit> AddPackagesForward(
        PackageInfo[] packages,
        PlanActionType actionType,
        VariableStore? variables,
        List<PlanAction> actions)
    {
        foreach (var package in packages)
        {
            var shouldInclude = EvaluateCondition(package, variables);
            if (shouldInclude.IsFailure)
                return Result<Unit>.Failure(shouldInclude.Error);

            if (!shouldInclude.Value)
                continue;

            actions.Add(new PlanAction
            {
                PackageId = package.Id,
                ActionType = actionType,
                Package = package
            });
        }

        return Unit.Value;
    }

    private static Result<Unit> AddPackagesReverse(
        PackageInfo[] packages,
        PlanActionType actionType,
        VariableStore? variables,
        List<PlanAction> actions)
    {
        // Reverse order for uninstall
        for (var i = packages.Length - 1; i >= 0; i--)
        {
            var package = packages[i];
            var shouldInclude = EvaluateCondition(package, variables);
            if (shouldInclude.IsFailure)
                return Result<Unit>.Failure(shouldInclude.Error);

            if (!shouldInclude.Value)
                continue;

            actions.Add(new PlanAction
            {
                PackageId = package.Id,
                ActionType = actionType,
                Package = package
            });
        }

        return Unit.Value;
    }

    private static void AddRelatedBundleUninstalls(
        IReadOnlyList<RelatedBundleInfo>? detectedRelatedBundles,
        List<PlanAction> actions)
    {
        if (detectedRelatedBundles is null || detectedRelatedBundles.Count == 0)
        {
            return;
        }

        foreach (var related in detectedRelatedBundles)
        {
            if (related.Relation != RelatedBundleRelation.Upgrade)
            {
                continue;
            }

            actions.Add(new PlanAction
            {
                PackageId = $"RelatedBundle_{related.BundleId}",
                ActionType = PlanActionType.Uninstall,
                Package = new PackageInfo
                {
                    Id = $"RelatedBundle_{related.BundleId}",
                    Type = PackageType.BundlePackage,
                    DisplayName = $"Related bundle {related.BundleId} v{related.InstalledVersion}",
                    Version = related.InstalledVersion,
                    SourcePath = string.Empty,
                    Sha256Hash = string.Empty,
                    Properties = related.RegistryKeyPath is not null
                        ? new Dictionary<string, string> { ["RegistryKeyPath"] = related.RegistryKeyPath }
                        : new Dictionary<string, string>()
                }
            });
        }
    }

    private static Result<bool> EvaluateCondition(PackageInfo package, VariableStore? variables)
    {
        if (string.IsNullOrWhiteSpace(package.InstallCondition))
            return true;

        if (variables is null)
            return true;

        return ConditionEvaluator.Evaluate(package.InstallCondition, variables);
    }
}
