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
/// <para>
/// <b>FK validation contract:</b> tables emitted by <see cref="IMultiTableProducer"/>
/// are <b>NOT</b> validated by <c>ForeignKeyValidator</c> or <c>PrimaryKeyValidator</c>.
/// Multi-producers run in Phase 5b — after the built-in producer pipeline and after
/// PK/FK validation of the fixed tables has already completed. Because their schemas
/// are user-defined and unknown at compile time, the validators cannot check them.
/// Authors of <see cref="IMultiTableProducer"/> implementations are solely responsible
/// for FK integrity within their emitted tables and between those tables and the
/// fixed built-in tables.
/// </para>
///
/// <para>
/// Security: producers that construct <see cref="TableId"/> or
/// <see cref="RecipeColumn"/> from user-supplied names benefit from the
/// identifier validation baked into those types. Defense-in-depth validation
/// (e.g. rejecting names that bypass structural checks) should be performed by
/// the producer before constructing the recipe types.
/// </para>
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
