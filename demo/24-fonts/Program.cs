using FalkForge;
using FalkForge.Compiler.Msi;

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

    package.Font("payload/DemoSans.ttf", f => { f.Title = "Demo Sans Regular"; });
}, new MsiCompiler());