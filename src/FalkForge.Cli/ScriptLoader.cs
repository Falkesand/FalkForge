using FalkForge.Builders;
using FalkForge.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace FalkForge.Cli;

/// <summary>
/// Loads C# installer definition files using Roslyn scripting and evaluates them
/// to produce <see cref="PackageModel"/> instances or compiled outputs.
/// </summary>
public static class ScriptLoader
{
    /// <summary>
    /// Loads a C# script and extracts the <see cref="PackageModel"/> without compiling.
    /// The script is expected to call <c>PackageBuilder</c> and return a <c>PackageModel</c>.
    /// </summary>
    public static Result<PackageModel> LoadPackageModel(string scriptPath)
    {
        try
        {
            var source = File.ReadAllText(scriptPath);
            var options = CreateScriptOptions();

            var result = CSharpScript.EvaluateAsync<PackageModel>(source, options).GetAwaiter().GetResult();
            if (result is null)
                return Result<PackageModel>.Failure(ErrorKind.CompilationError,
                    "Script did not return a PackageModel. Ensure the script evaluates to a PackageModel instance.");

            return result;
        }
        catch (CompilationErrorException ex)
        {
            var diagnostics = string.Join(Environment.NewLine, ex.Diagnostics);
            return Result<PackageModel>.Failure(ErrorKind.CompilationError,
                $"Script compilation failed:{Environment.NewLine}{diagnostics}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<PackageModel>.Failure(ErrorKind.ExecutionError,
                $"Script execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a C# script, evaluates it, and builds the installer output.
    /// </summary>
    public static Result<string> LoadAndBuild(string scriptPath, string outputPath, string configuration)
    {
        try
        {
            var source = File.ReadAllText(scriptPath);
            var options = CreateScriptOptions();

            // The script is expected to return a PackageModel
            var package = CSharpScript.EvaluateAsync<PackageModel>(source, options).GetAwaiter().GetResult();
            if (package is null)
                return Result<string>.Failure(ErrorKind.CompilationError,
                    "Script did not return a PackageModel. Ensure the script evaluates to a PackageModel instance.");

            // Validate first
            var validation = Validation.ModelValidator.Validate(package);
            if (!validation.IsValid)
            {
                var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
                return Result<string>.Failure(ErrorKind.Validation, $"Validation failed: {errors}");
            }

            return Result<string>.Failure(ErrorKind.NotSupported,
                "Direct compilation from CLI is not yet supported. Use 'forge validate' to check your project.");
        }
        catch (CompilationErrorException ex)
        {
            var diagnostics = string.Join(Environment.NewLine, ex.Diagnostics);
            return Result<string>.Failure(ErrorKind.CompilationError,
                $"Script compilation failed:{Environment.NewLine}{diagnostics}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<string>.Failure(ErrorKind.ExecutionError,
                $"Script execution failed: {ex.Message}");
        }
    }

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(typeof(PackageModel).Assembly)
            .AddReferences(typeof(PackageBuilder).Assembly)
            .AddImports(
                "FalkForge",
                "FalkForge.Models",
                "FalkForge.Builders");
    }
}
