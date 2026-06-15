using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// Sample installer: a single text file deployed to Program Files.
// This MSI is the input that `forge migrate` will convert into a FalkForge C# project.
return Installer.Build(args, package =>
{
    package.Name = "Forge Migrate Sample";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    package.MediaTemplate(mt =>
    {
        mt.CabinetTemplate("data{0}.cab");
        mt.CompressionLevel(CompressionLevel.High);
        mt.EmbedCabinet(true);
    });

    package.Reproducible();

    package.Files(files => files
        .Add("payload/readme.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "ForgeMigrateSample"));
}, new MsiCompiler());
