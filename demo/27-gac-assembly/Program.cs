using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Register a .NET assembly in the Global Assembly Cache (GAC).
return Installer.Build(args, package =>
{
    package.Name = "GAC Assembly Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/MyLib.dll")
        .To(KnownFolder.ProgramFiles / "Demo" / "GacDemo"));

    package.GacAssembly(asm => asm
        .FileRef("MyLib.dll")
        .Type(AssemblyType.DotNetAssembly));
}, new MsiCompiler());