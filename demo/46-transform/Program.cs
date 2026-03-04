using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;

// Create a transform (.mst) to customize MSI properties at deployment time.
return Installer.BuildTransform(args, transform =>
{
    transform.BaseMsi("payload/base.msi");
    transform.Description("Enterprise Customization");

    // Override properties for enterprise deployment
    transform.SetProperty("ALLUSERS", "1");
    transform.SetProperty("INSTALLDIR", @"D:\Apps\MyApp");
    transform.SetProperty("REBOOT", "ReallySuppress");

}, (model, outputPath) => new TransformCompiler().Compile(model, outputPath));
