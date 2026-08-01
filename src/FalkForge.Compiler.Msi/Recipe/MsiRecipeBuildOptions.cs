namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Knob bag controlling <see cref="MsiRecipeBuilder"/> behavior. Currently has
/// no active knobs — retained as the extension point for future
/// <see cref="MsiRecipeBuilder"/> tuning so callers keep passing
/// <c>new MsiRecipeBuildOptions()</c> without a breaking API change when one is
/// added. See task #27 for the eager-stream-hashing / in-memory-threshold
/// specification that was removed unimplemented.
/// </summary>
public sealed record MsiRecipeBuildOptions;
