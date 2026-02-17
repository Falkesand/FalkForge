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
        IReadOnlyList<RelatedBundleInfo>? detectedRelatedBundles = null,
        IReadOnlyDictionary<string, bool>? featureSelections = null)
    {
        var actions = new List<PlanAction>();

        // Build feature-to-package lookup: packageId -> set of featureIds that reference it
        var packageFeatureMap = BuildPackageFeatureMap(manifest.Features);

        switch (action)
        {
            case InstallAction.Install:
            {
                // Plan uninstall of upgrade-related bundles before installing new packages
                AddRelatedBundleUninstalls(detectedRelatedBundles, actions);

                var result = AddPackagesForward(manifest.Packages, PlanActionType.Install, variables, actions, packageFeatureMap, featureSelections);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Uninstall:
            {
                // Uninstall ignores feature selections — all packages are candidates
                var result = AddPackagesReverse(manifest.Packages, PlanActionType.Uninstall, variables, actions, packageFeatureMap: null, featureSelections: null);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Repair:
            {
                var result = AddPackagesForward(manifest.Packages, PlanActionType.Repair, variables, actions, packageFeatureMap, featureSelections);
                if (result.IsFailure)
                    return Result<InstallPlan>.Failure(result.Error);
                break;
            }

            case InstallAction.Modify:
            {
                var result = AddPackagesForward(manifest.Packages, PlanActionType.Install, variables, actions, packageFeatureMap, featureSelections);
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
        List<PlanAction> actions,
        Dictionary<string, HashSet<string>>? packageFeatureMap,
        IReadOnlyDictionary<string, bool>? featureSelections)
    {
        foreach (var package in packages)
        {
            if (!IsPackageSelectedByFeatures(package.Id, packageFeatureMap, featureSelections))
                continue;

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
        List<PlanAction> actions,
        Dictionary<string, HashSet<string>>? packageFeatureMap,
        IReadOnlyDictionary<string, bool>? featureSelections)
    {
        // Reverse order for uninstall
        for (var i = packages.Length - 1; i >= 0; i--)
        {
            var package = packages[i];

            if (!IsPackageSelectedByFeatures(package.Id, packageFeatureMap, featureSelections))
                continue;

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

    /// <summary>
    /// Builds a lookup from package ID to the set of feature IDs that reference it.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildPackageFeatureMap(ManifestFeature[] features)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            foreach (var packageId in feature.PackageIds)
            {
                if (!map.TryGetValue(packageId, out var featureIds))
                {
                    featureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[packageId] = featureIds;
                }

                featureIds.Add(feature.Id);
            }
        }

        return map;
    }

    /// <summary>
    /// Determines whether a package should be included based on feature selections.
    /// Returns true if:
    ///   - No feature system is defined (no features at all)
    ///   - The package is not referenced by any feature (not feature-gated)
    ///   - The package is referenced by at least one selected feature
    /// Returns false if:
    ///   - The package is feature-gated but no selections are provided
    ///   - The package is feature-gated and no referencing feature is selected
    /// </summary>
    private static bool IsPackageSelectedByFeatures(
        string packageId,
        Dictionary<string, HashSet<string>>? packageFeatureMap,
        IReadOnlyDictionary<string, bool>? featureSelections)
    {
        // No feature system defined at all
        if (packageFeatureMap is null || packageFeatureMap.Count == 0)
            return true;

        // Features defined but package not in any feature — always included
        if (!packageFeatureMap.TryGetValue(packageId, out var featureIds))
            return true;

        // Features defined, package is feature-gated, but no selections — exclude feature-gated packages
        if (featureSelections is null || featureSelections.Count == 0)
            return false;

        // Normal path: check if any referencing feature is selected
        foreach (var featureId in featureIds)
        {
            if (featureSelections.TryGetValue(featureId, out var selected) && selected)
                return true;
        }

        return false;
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
