namespace FalkInstaller;

using FalkInstaller.Builders;
using FalkInstaller.Models;

public static class Installer
{
    public static int Build(string[] args, Action<PackageBuilder> configure, ICompiler? compiler = null)
    {
        var builder = new PackageBuilder();
        configure(builder);
        var package = builder.Build();

        var validation = Validation.ModelValidator.Validate(package);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                Console.Error.WriteLine($"Error {error.Code}: {error.Message}");
            }
            return 1;
        }

        foreach (var warning in validation.Warnings)
        {
            Console.Error.WriteLine($"Warning {warning.Code}: {warning.Message}");
        }

        if (compiler is not null)
        {
            var outputPath = GetOutputPath(args);
            var result = compiler.Compile(package, outputPath);
            if (result.IsFailure)
            {
                Console.Error.WriteLine($"Compilation failed: {result.Error}");
                return 1;
            }
            Console.WriteLine($"Package created: {result.Value}");
        }

        return 0;
    }

    /// <summary>
    /// Builds a bundle installer package.
    /// The caller handles model construction and validation, then passes a compile function
    /// that receives the resolved output path and returns the created file path.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="compile">
    /// A function that receives the output path and returns a <see cref="Result{T}"/>
    /// containing the created bundle file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int BuildBundle(string[] args, Func<string, Result<string>> compile)
    {
        var outputPath = GetOutputPath(args);
        var result = compile(outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Bundle compilation failed: {result.Error}");
            return 1;
        }
        Console.WriteLine($"Bundle created: {result.Value}");
        return 0;
    }

    /// <summary>
    /// Builds a merge module (.msm) package.
    /// Configures the merge module model via a fluent builder, validates it,
    /// and passes the model and output path to the compile function.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the merge module builder.</param>
    /// <param name="compile">
    /// A function that receives the merge module model and output path,
    /// and returns the created .msm file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int BuildMergeModule(string[] args, Action<MergeModuleBuilder> configure, Func<MergeModuleModel, string, Result<string>> compile)
    {
        var builder = new MergeModuleBuilder();
        configure(builder);
        var buildResult = builder.Build();

        if (buildResult.IsFailure)
        {
            Console.Error.WriteLine($"Merge module validation failed: {buildResult.Error}");
            return 1;
        }

        var model = buildResult.Value;
        var outputPath = GetOutputPath(args);
        var result = compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Merge module compilation failed: {result.Error}");
            return 1;
        }
        Console.WriteLine($"Merge module created: {result.Value}");
        return 0;
    }

    /// <summary>
    /// Builds a patch (.msp) package.
    /// Configures the patch model via a fluent builder, validates it,
    /// and passes the model and output path to the compile function.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the patch builder.</param>
    /// <param name="compile">
    /// A function that receives the patch model and output path,
    /// and returns the created .msp file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int BuildPatch(string[] args, Action<PatchBuilder> configure, Func<PatchModel, string, Result<string>> compile)
    {
        var builder = new PatchBuilder();
        configure(builder);
        var buildResult = builder.Build();

        if (buildResult.IsFailure)
        {
            Console.Error.WriteLine($"Patch validation failed: {buildResult.Error}");
            return 1;
        }

        var model = buildResult.Value;
        var outputPath = GetOutputPath(args);
        var result = compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Patch compilation failed: {result.Error}");
            return 1;
        }
        Console.WriteLine($"Patch created: {result.Value}");
        return 0;
    }

    /// <summary>
    /// Builds a transform (.mst) file.
    /// Configures the transform model via a fluent builder, validates it,
    /// and passes the model and output path to the compile function.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the transform builder.</param>
    /// <param name="compile">
    /// A function that receives the transform model and output path,
    /// and returns the created .mst file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int BuildTransform(string[] args, Action<TransformBuilder> configure, Func<TransformModel, string, Result<string>> compile)
    {
        var builder = new TransformBuilder();
        configure(builder);
        var buildResult = builder.Build();

        if (buildResult.IsFailure)
        {
            Console.Error.WriteLine($"Transform validation failed: {buildResult.Error}");
            return 1;
        }

        var model = buildResult.Value;
        var outputPath = GetOutputPath(args);
        var result = compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Transform compilation failed: {result.Error}");
            return 1;
        }
        Console.WriteLine($"Transform created: {result.Value}");
        return 0;
    }

    private static string GetOutputPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "-o" or "--output")
                return args[i + 1];
        }
        return Directory.GetCurrentDirectory();
    }
}
