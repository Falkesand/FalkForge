using FalkForge.Models;

namespace FalkForge.Validation;

public static class ModelValidator
{
    private static readonly ValidationEngine _packageEngine
        = new(CoreRuleCatalog.Package);

    /// <summary>
    /// Zero-allocation happy-path check. Returns <see cref="Result{Unit}.Success"/> on a
    /// clean package; aggregates all error messages into a single <see cref="Result{Unit}.Failure"/>
    /// on the first violation. Uses the <see cref="CoreRuleCatalog.Package"/> registry.
    /// </summary>
    public static Result<Unit> Check(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _packageEngine.Run(package).ToResult();
    }

    /// <summary>
    /// Rich validation report with warnings, structured locations, and rule metadata.
    /// Used by CLI <c>forge validate</c> and test utilities that need per-violation detail.
    /// </summary>
    public static ValidationReport Inspect(PackageModel package, ValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _packageEngine.Run(package, options);
    }

    /// <summary>
    /// Returns the complete list of rules registered for the given <paramref name="target"/>.
    /// Powers <c>forge validate --list-rules</c> and Studio tooling.
    /// </summary>
    public static IReadOnlyList<ValidationRule> ListRules(ValidationTarget target = ValidationTarget.Package)
    {
        var catalog = target switch
        {
            ValidationTarget.Package     => CoreRuleCatalog.Package,
            ValidationTarget.MergeModule => CoreRuleCatalog.MergeModule,
            ValidationTarget.Patch       => CoreRuleCatalog.Patch,
            ValidationTarget.Transform   => CoreRuleCatalog.Transform,
            _                            => CoreRuleCatalog.Package
        };
        return catalog.Rules;
    }
}
