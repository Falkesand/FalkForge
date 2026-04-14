using FalkForge.Models;

namespace FalkForge.Cli;

/// <summary>
/// Dispatches a build input file to the appropriate loader based on its extension
/// and returns a <see cref="PackageModel"/> ready for compilation.
/// Keeps <see cref="Commands.BuildCommand"/> thin: it should not have to know
/// how a .cs/.csx script differs from a .json config.
/// </summary>
/// <remarks>
/// Extension mapping:
/// <list type="bullet">
///   <item><c>.cs</c>, <c>.csx</c> — Roslyn scripting via <see cref="ScriptLoader"/>.</item>
///   <item><c>.json</c> — declarative config via <see cref="JsonConfigLoader"/>.</item>
/// </list>
/// Unknown extensions yield <see cref="ErrorKind.InvalidConfiguration"/> so the CLI
/// can distinguish a bad project argument from a loader-level failure.
/// </remarks>
public static class BuildInputResolver
{
    public static Result<PackageModel> Load(string projectPath)
    {
        if (projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return JsonConfigLoader.LoadFromFile(projectPath);

        if (projectPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            projectPath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase))
            return ScriptLoader.LoadPackageModel(projectPath);

        return Result<PackageModel>.Failure(new Error(
            ErrorKind.InvalidConfiguration,
            $"Unsupported input extension for '{projectPath}'. Expected .cs, .csx, or .json."));
    }
}
