using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;

// Create a patch (.msp) that upgrades from v1 to v2 of an MSI.
return Installer.BuildPatch(args, patch =>
{
    patch.Manufacturer("Demo");
    patch.Description("Updates App from 1.0 to 1.1");
    patch.TargetMsi("payload/app-v1.msi");
    patch.UpdatedMsi("payload/app-v2.msi");

}, (model, outputPath) => new PatchCompiler().Compile(model, outputPath));
