using System.Collections.Immutable;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Merges extension contributors (and their execution steps) into the
/// built-in table set.
/// </summary>
public static partial class MsiRecipeBuilder
{
    private static Result<(ImmutableArray<RecipeTable> BuiltInTables, ImmutableArray<RecipeTable> CustomTables)> ApplyExtensionContributors(
        IReadOnlyList<IMsiTableContributor> contributors,
        IReadOnlyList<IExecutionContributor>? executionContributors,
        ImmutableArray<RecipeTable> builtInTables,
        ExtensionContext emitContext,
        RecipeBuildContext context,
        IFalkLogger? logger)
    {
        ImmutableArray<RecipeTable> extensionCustomTables = ImmutableArray<RecipeTable>.Empty;

        // Translate extension-contributed execution steps into synthetic CustomAction +
        // InstallExecuteSequence contributors. Steps from every contributor are gathered in
        // registration order so ExecutionStepEmitter allocates sequence numbers from a single
        // deterministic pool — multiple execution-contributing extensions in one package cannot
        // collide. The synthetic contributors merge into the built-in CustomAction /
        // InstallExecuteSequence tables through the same path as any other contributor.
        IReadOnlyList<IMsiTableContributor> effectiveContributors = contributors;
        if (executionContributors is { Count: > 0 })
        {
            var steps = new List<ExecutionStep>();
            foreach (IExecutionContributor executionContributor in executionContributors)
            {
                IReadOnlyList<ExecutionStep> contributed = executionContributor.GetExecutionSteps(emitContext);
                if (contributed is { Count: > 0 })
                    steps.AddRange(contributed);
            }

            if (steps.Count > 0)
            {
                Result<ImmutableArray<IMsiTableContributor>> execResult =
                    ExecutionStepEmitter.BuildContributors(steps);
                if (execResult.IsFailure)
                {
                    logger?.Log(LogLevel.Error, "MsiRecipeBuilder",
                        $"Execution step emission failed: {execResult.Error.Message}",
                        new Dictionary<string, string> { ["code"] = execResult.Error.Kind.ToString() });
                    return Result<(ImmutableArray<RecipeTable>, ImmutableArray<RecipeTable>)>.Failure(execResult.Error);
                }

                var merged = new List<IMsiTableContributor>(contributors.Count + execResult.Value.Length);
                merged.AddRange(contributors);
                merged.AddRange(execResult.Value);
                effectiveContributors = merged;
            }
        }

        if (effectiveContributors.Count > 0)
        {
            Result<ExtensionTableEmitter.EmissionOutcome> emitResult = ExtensionTableEmitter.Emit(
                effectiveContributors, builtInTables, emitContext, context.Streams, logger);
            if (emitResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiRecipeBuilder",
                    $"Extension table emission failed: {emitResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = emitResult.Error.Kind.ToString() });
                return Result<(ImmutableArray<RecipeTable>, ImmutableArray<RecipeTable>)>.Failure(emitResult.Error);
            }

            builtInTables = emitResult.Value.BuiltInTables;
            extensionCustomTables = emitResult.Value.CustomTables;
        }

        return Result<(ImmutableArray<RecipeTable>, ImmutableArray<RecipeTable>)>.Success((builtInTables, extensionCustomTables));
    }
}
