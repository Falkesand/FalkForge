using System.Collections.Immutable;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Pure function that turns a <see cref="ResolvedPackage"/> plus any extension
/// table contributors into an immutable <see cref="MsiDatabaseRecipe"/>.
///
/// Phase 3 ships an empty pipeline: no producers run, no tables are emitted,
/// no streams are registered. The skeleton exists so phase 4+ can plug in
/// individual <see cref="ITableProducer"/> implementations against a stable
/// public surface and a tested orchestration shell.
/// </summary>
public static class MsiRecipeBuilder
{
    /// <summary>
    /// Build a recipe from the resolved package, extension contributors, and
    /// build options. Returns <see cref="ErrorKind.Validation"/> failure for
    /// any null argument; otherwise returns a success result wrapping a recipe
    /// with empty tables, an empty stream dictionary, an empty sequencing
    /// array, default zero-valued summary info, and an empty content hash.
    /// </summary>
    public static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options)
    {
        if (resolved is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Resolved package cannot be null.");
        }

        if (contributors is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Contributors cannot be null.");
        }

        if (options is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Options cannot be null.");
        }

        // Phase 3: empty pipeline. Real producers wire in later phases via
        // RecipeBuildContext + ITableProducer; for now the builder validates
        // inputs and returns a structurally-valid empty recipe so downstream
        // executor scaffolding can be developed in parallel.
        SummaryInfoRecipe summaryInfo = new()
        {
            Title = string.Empty,
            Subject = string.Empty,
            Author = string.Empty,
            Template = string.Empty,
            Keywords = string.Empty,
            Comments = string.Empty,
            RevisionNumber = 0,
            CodePage = 1252,
        };

        MsiDatabaseRecipe recipe = new()
        {
            Tables = ImmutableArray<RecipeTable>.Empty,
            SummaryInfo = summaryInfo,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = null,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };

        return Result<MsiDatabaseRecipe>.Success(recipe);
    }
}
