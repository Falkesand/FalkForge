using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Pure function from <see cref="RecipeBuildContext"/> to the rows of a single
/// MSI table. Each producer owns one <see cref="TableSchema"/> and is responsible
/// for emitting deterministically ordered rows derived from the resolved
/// package or upstream built tables. Real producers arrive in later phases;
/// phase 3 ships only the contract.
/// </summary>
internal interface ITableProducer
{
    /// <summary>The schema of the table this producer emits.</summary>
    TableSchema Schema { get; }

    /// <summary>Compute the rows for this producer's table from the build context.</summary>
    Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context);
}
