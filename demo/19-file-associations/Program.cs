using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Register a file extension so double-clicking .demo files opens our app.
return Installer.Build(args, package =>
{
    package.Name = "File Associations Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FileAssocDemo"));

    // Associate .demo files with our application
    package.FileAssociation(".demo", fa =>
    {
        fa.ContentType = "application/x-demo";
        fa.Description = "Demo Document";
        fa.IconFile = "payload/app.exe";
        fa.IconIndex = 0;

        fa.Verb(ShellVerb.Open, "\"%1\"", verb => { verb.Command = "Open"; });
    });
}, new MsiCompiler());