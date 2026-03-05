using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Set system and user environment variables during installation.
return Installer.Build(args, package =>
{
    package.Name = "Environment Variables Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "EnvVarDemo"));

    // Set a new system-level variable
    package.EnvironmentVariable("DEMO_HOME", @"[ProgramFilesFolder]Demo\EnvVarDemo", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Set;
    });

    // Append to the system PATH
    package.EnvironmentVariable("PATH", @"[ProgramFilesFolder]Demo\EnvVarDemo", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Append;
        env.Separator = ";";
    });

    // User-scoped variable (not system-wide)
    package.EnvironmentVariable("DEMO_USER_PREF", "enabled", ev =>
    {
        ev.IsSystem = false;
        ev.Action = EnvironmentVariableAction.Set;
    });
}, new MsiCompiler());