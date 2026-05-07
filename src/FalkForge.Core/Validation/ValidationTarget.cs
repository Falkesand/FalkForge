namespace FalkForge.Validation;

/// <summary>
/// The top-level model type that a rule catalog targets.
/// </summary>
public enum ValidationTarget
{
    Package,
    MergeModule,
    Patch,
    Transform
}
