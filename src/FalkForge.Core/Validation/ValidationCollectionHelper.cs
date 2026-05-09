using System.Collections.Immutable;

namespace FalkForge.Validation;

/// <summary>
/// Shared helper for validation rules that iterate a collection and accumulate violations.
/// Eliminates the repetitive CreateBuilder / for-loop / ToImmutable pattern across MiscRules
/// and similar per-area rule files.
/// </summary>
internal static class ValidationCollectionHelper
{
    /// <summary>
    /// Iterates <paramref name="items"/> in order, calling <paramref name="check"/> for each
    /// element with the element value and its zero-based index. Non-null results are collected
    /// and returned as an <see cref="ImmutableArray{Violation}"/>.
    /// </summary>
    /// <typeparam name="T">Collection element type.</typeparam>
    /// <param name="items">The collection to validate.</param>
    /// <param name="check">
    /// Per-element check: returns a <see cref="Violation"/> when the element fails the rule,
    /// or <see langword="null"/> when it passes.
    /// </param>
    /// <returns>
    /// All violations produced by <paramref name="check"/>, in iteration order.
    /// Returns an empty array when no violations are found.
    /// </returns>
    internal static ImmutableArray<Violation> ValidateCollection<T>(
        IReadOnlyList<T> items,
        Func<T, int, Violation?> check)
    {
        ImmutableArray<Violation>.Builder? builder = null;

        for (var i = 0; i < items.Count; i++)
        {
            var violation = check(items[i], i);
            if (violation is not null)
                (builder ??= ImmutableArray.CreateBuilder<Violation>()).Add(violation);
        }

        return builder?.ToImmutable() ?? [];
    }
}
