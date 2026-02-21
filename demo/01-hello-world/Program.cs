using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// The simplest possible installer: one file, no features, Minimal dialog set.
return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));

}, new MsiCompiler());
