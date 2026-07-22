using System.Collections.Immutable;
using FalkForge.Diagnostics;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Primary-key / foreign-key validation of the built-in tables, and execution
/// of the multi-table producers (dynamic-schema tables such as user-defined
/// custom tables) that run after that validation.
/// </summary>
public static partial class MsiRecipeBuilder
{
    private static Result<ImmutableArray<RecipeTable>> ValidateBuiltInTables(
        ImmutableArray<RecipeTable> builtInTables,
        IFalkLogger? logger)
    {
        Result<Unit> pkResult = PrimaryKeyValidator.Validate(builtInTables);
        if (pkResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, "MsiRecipeBuilder", $"Primary-key validation failed: {pkResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = pkResult.Error.Kind.ToString() });
            return Result<ImmutableArray<RecipeTable>>.Failure(pkResult.Error);
        }

        Result<Unit> fkResult = ForeignKeyValidator.Validate(builtInTables);
        if (fkResult.IsFailure)
        {
            logger?.Log(LogLevel.Error, "MsiRecipeBuilder", $"Foreign-key validation failed: {fkResult.Error.Message}",
                new Dictionary<string, string> { ["code"] = fkResult.Error.Kind.ToString() });
            return Result<ImmutableArray<RecipeTable>>.Failure(fkResult.Error);
        }

        return Result<ImmutableArray<RecipeTable>>.Success(builtInTables);
    }

    // FK validation gap — by design: ValidateBuiltInTables above runs only over the fixed
    // built-in tables. Tables emitted by multi-table producers are NOT checked. Each
    // IMultiTableProducer implementation is solely responsible for FK integrity within its
    // tables and against the built-in tables. See IMultiTableProducer XML doc for the full
    // contract. Extension custom tables share the same exemption: their schemas are not known
    // to the fixed-table validators.
    private static Result<ImmutableArray<RecipeTable>> RunMultiTableProducersAndDrainWarnings(
        RecipeBuildContext context,
        ImmutableArray<RecipeTable> validatedTables,
        ImmutableArray<RecipeTable> extensionCustomTables,
        IReadOnlyList<IMultiTableProducer> multiProducers,
        bool logProducerDebug,
        IFalkLogger? logger)
    {
        ImmutableArray<RecipeTable>.Builder finalBuilder = ImmutableArray.CreateBuilder<RecipeTable>(
            validatedTables.Length + extensionCustomTables.Length + multiProducers.Count);
        finalBuilder.AddRange(validatedTables);
        finalBuilder.AddRange(extensionCustomTables);

        foreach (IMultiTableProducer multiProducer in multiProducers)
        {
            Result<ImmutableArray<RecipeTable>> multiResult = multiProducer.Produce(context);
            if (multiResult.IsFailure)
            {
                logger?.Log(LogLevel.Error, "MsiRecipeBuilder",
                    $"Multi-table producer '{multiProducer.GetType().Name}' failed: {multiResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = multiResult.Error.Kind.ToString() });
                return Result<ImmutableArray<RecipeTable>>.Failure(multiResult.Error);
            }

            if (logProducerDebug)
                logger!.Debug("MsiRecipeBuilder",
                    $"Multi-table producer '{multiProducer.GetType().Name}' produced {multiResult.Value.Length} table(s).");

            finalBuilder.AddRange(multiResult.Value);
        }

        // Drain any non-fatal diagnostics producers queued on the context (e.g. DialogSetProducer's
        // DLG004 LicenseFile-vs-dialog-set mismatch). Producers only see RecipeBuildContext, not the
        // IFalkLogger passed to MsiRecipeBuilder.Build, so this is where the two are reconnected.
        foreach ((string code, string message) in context.Warnings)
        {
            logger?.Log(LogLevel.Warning, "MsiRecipeBuilder", message,
                new Dictionary<string, string> { ["code"] = code });
        }

        return Result<ImmutableArray<RecipeTable>>.Success(finalBuilder.ToImmutable());
    }
}
