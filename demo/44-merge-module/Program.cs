using FalkForge;
using FalkForge.Compiler.Msi;

// Create a merge module (.msm) — a reusable component package.
return Installer.BuildMergeModule(args, module =>
{
    module.Id(new Guid("A1B2C3D4-E5F6-7A8B-9C0D-E1F2A3B4C5D6"));
    module.Version(new Version(1, 0, 0));
    module.Manufacturer("Demo");
    module.Language(1033);
    module.Description("Shared components merge module");

    // A merge module must include at least one component
    module.Component("SharedRuntime");

    module.Dependency("SharedRuntime_1.0");
}, (model, outputPath) => new MsmCompiler().Compile(model, outputPath));