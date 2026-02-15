using FalkInstaller;
using FalkInstaller.Builders;
using FalkInstaller.Compiler.Msi;
using FalkInstaller.Models;
using FalkInstaller.Platform.Windows;

// The simplest possible installer: one file, no features, Minimal dialog set.
return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));

}, new MsiCompiler(new WindowsFileSystem()));
