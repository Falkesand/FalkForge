namespace FalkInstaller;

using FalkInstaller.Builders;
using FalkInstaller.Models;

public static class Installer
{
    public static int Build(string[] args, Action<PackageBuilder> configure)
    {
        var builder = new PackageBuilder();
        configure(builder);
        var package = builder.Build();

        // In Phase 1, we validate and return the model.
        // The actual MSI compilation will be handled by FalkInstaller.Compiler.Msi
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

        // TODO: Pass to MsiCompiler when available
        return 0;
    }
}
