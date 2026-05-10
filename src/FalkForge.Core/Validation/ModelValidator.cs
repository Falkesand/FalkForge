using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

public static class ModelValidator
{
    // _packageEngine is replaced (not mutated) by RegisterExtensionRules.
    // Volatile ensures the latest reference is visible across threads without a full lock on reads.
    private static volatile ValidationEngine _packageEngine
        = new(CoreRuleCatalog.Package);

    private static readonly object _extensionRulesLock = new();

    /// <summary>
    /// Zero-allocation happy-path check. Returns <see cref="Result{Unit}.Success"/> on a
    /// clean package; aggregates all error messages into a single <see cref="Result{Unit}.Failure"/>
    /// on the first violation. Uses the <see cref="CoreRuleCatalog.Package"/> registry plus
    /// any rules contributed via <see cref="RegisterExtensionRules"/>.
    /// </summary>
    public static Result<Unit> Check(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _packageEngine.Run(package).ToResult();
    }

    /// <summary>
    /// Rich validation report with warnings, structured locations, and rule metadata.
    /// Used by CLI <c>forge validate</c> and test utilities that need per-violation detail.
    /// Includes rules contributed via <see cref="RegisterExtensionRules"/>.
    /// </summary>
    public static ValidationReport Inspect(PackageModel package, ValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _packageEngine.Run(package, options);
    }

    /// <summary>
    /// Merges extension-contributed <see cref="ValidationRule"/> instances into the singleton
    /// package engine. Called by <c>MsiAuthoring.Compile</c> (and other compile entry points)
    /// after extensions are registered so their rules fire alongside core rules on the next
    /// <see cref="Inspect"/> call.
    /// </summary>
    /// <remarks>
    /// Thread-safe: uses a lock around the replace-not-mutate swap.
    /// Idempotent per rule set but accumulative across distinct call sites —
    /// call once per compilation, not once per rule.
    /// </remarks>
    public static void RegisterExtensionRules(IEnumerable<ValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var ruleArray = rules.ToArray();
        if (ruleArray.Length == 0)
            return;

        lock (_extensionRulesLock)
        {
            // Deduplicate: skip rules whose ID is already registered (idempotent re-registration).
            // This prevents FrozenDictionary duplicate-key exceptions when the same extension
            // is registered across multiple test runs against the same process-wide singleton.
            var existing = CoreRuleCatalog.Package.ById;
            var newOnly = ruleArray.Where(r => !existing.ContainsKey(r.Id)).ToArray();
            if (newOnly.Length == 0)
                return;

            // Replace-not-mutate: build a new registry with the extra rules appended.
            // Reads (_packageEngine) are volatile so callers see the new engine immediately.
            var extended = CoreRuleCatalog.Package.WithAdded(newOnly);
            CoreRuleCatalog.Package = extended;
            _packageEngine = new ValidationEngine(extended);
        }
    }

    /// <summary>
    /// Returns the complete list of rules registered for the given <paramref name="target"/>.
    /// Powers <c>forge validate --list-rules</c> and Studio tooling.
    /// Includes extension rules registered via <see cref="RegisterExtensionRules"/>.
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
