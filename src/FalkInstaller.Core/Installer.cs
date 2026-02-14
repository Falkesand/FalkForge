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
