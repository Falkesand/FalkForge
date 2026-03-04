using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Register a TrueType font during installation.
return Installer.Build(args, package =>
{
    package.Name = "Fonts Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/demofont.ttf")
        .To(KnownFolder.FontsFolder / "DemoFonts"));

    package.Font("demofont.ttf");

}, new MsiCompiler());
