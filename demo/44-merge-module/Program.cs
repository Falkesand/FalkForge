using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Create a merge module (.msm) — a reusable component package.
return Installer.BuildMergeModule(args, module =>
{
    module.Version(new Version(1, 0, 0));
    module.Manufacturer("Demo");
    module.Language(1033);
    module.Description("Shared components merge module");

}, (model, outputPath) => new MsmCompiler().Compile(model, outputPath));
