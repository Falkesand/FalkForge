namespace FalkInstaller.Engine.Planning;

using FalkInstaller.Engine.Detection;
using FalkInstaller.Engine.Protocol.Manifest;

public sealed class Planner
{
    public Result<InstallPlan> CreatePlan(InstallerManifest manifest, DetectionResult detection, InstallAction action)
    {
        var actions = new List<PlanAction>();

        switch (action)
        {
            case InstallAction.Install:
                foreach (var package in manifest.Packages)
                {
                    actions.Add(new PlanAction
                    {
                        PackageId = package.Id,
                        ActionType = PlanActionType.Install,
                        Package = package
                    });
                }
                break;

            case InstallAction.Uninstall:
                // Reverse order for uninstall
                for (var i = manifest.Packages.Length - 1; i >= 0; i--)
                {
                    var package = manifest.Packages[i];
                    actions.Add(new PlanAction
                    {
                        PackageId = package.Id,
                        ActionType = PlanActionType.Uninstall,
                        Package = package
                    });
                }
                break;

            case InstallAction.Repair:
                foreach (var package in manifest.Packages)
                {
                    actions.Add(new PlanAction
                    {
                        PackageId = package.Id,
                        ActionType = PlanActionType.Repair,
                        Package = package
                    });
                }
                break;

            case InstallAction.Modify:
                // For modify, install/uninstall based on feature selection (simplified for now)
                foreach (var package in manifest.Packages)
                {
                    actions.Add(new PlanAction
                    {
                        PackageId = package.Id,
                        ActionType = PlanActionType.Install,
                        Package = package
                    });
                }
                break;

            default:
                return Result<InstallPlan>.Failure(ErrorKind.PlanningError, $"Unknown action: {action}");
        }

        return new InstallPlan
        {
            Actions = actions,
            TotalDiskSpaceRequired = 0 // Would calculate from package sizes
        };
    }
}
