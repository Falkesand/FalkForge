using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Produces zero or more <see cref="RecipeTable"/> instances from a single
/// <see cref="RecipeBuildContext"/>. Unlike <see cref="ITableProducer"/>,
/// which owns exactly one fixed table schema, a multi-table producer dynamically
/// determines both the number of tables and their schemas at build time.
/// The canonical use case is <c>CustomTablesProducer</c>, which emits one
/// <see cref="RecipeTable"/> per <see cref="FalkForge.Models.CustomTableModel"/>
/// defined on the package.
///
/// Security: producers that construct <see cref="TableId"/> or
/// <see cref="RecipeColumn"/> from user-supplied names benefit from the
/// identifier validation baked into those types. Defense-in-depth validation
/// (e.g. rejecting names that bypass structural checks) should be performed by
/// the producer before constructing the recipe types.
/// </summary>
internal interface IMultiTableProducer
{
    /// <summary>
    /// Compute all tables this producer emits for the given build context.
    /// Returns an empty array to signal "no tables"; returns a failure result
    /// to abort the build with a descriptive error.
    /// </summary>
    Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context);
}
