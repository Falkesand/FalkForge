using FalkForge;
using FalkForge.Compiler.Msi;

// Block installation unless conditions are met: admin rights and Windows 10+.
return Installer.Build(args, package =>
{
    package.Name = "Launch Conditions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "LaunchCondDemo"));

    // Require administrator privileges
    package.Require(Condition.IsPrivileged, "This application requires administrator privileges.");

    // Require Windows 10 or later
    package.Require(Condition.IsWindows10OrLater, "This application requires Windows 10 or later.");
}, new MsiCompiler());