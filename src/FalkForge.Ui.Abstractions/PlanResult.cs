namespace FalkForge.Ui.Abstractions;

public readonly record struct PlanResult(string[] PackageActions, long TotalDiskSpaceRequired);
