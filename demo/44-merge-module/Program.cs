using FalkForge;
using FalkForge.Compiler.Msi;

// Create a merge module (.msm) — a reusable component package.
return Installer.BuildMergeModule(args, module =>
{
    module.Version(new Version(1, 0, 0));
    module.Manufacturer("Demo");
    module.Language(1033);
    module.Description("Shared components merge module");

    // A merge module must include at least one component
    module.Component("SharedRuntime");

    module.Dependency("SharedRuntime_1.0");
}, (model, outputPath) => new MsmCompiler().Compile(model, outputPath));