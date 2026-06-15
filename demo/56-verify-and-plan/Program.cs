using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// Provability Demo — a minimal reproducible MSI used with forge verify and forge plan-diff.
//
// Key properties:
//   - Reproducible() pins the PackageCode to a content-derived UUID so the same source always
//     produces an identical MSI (no random GUIDs, no embedded timestamps).
//   - This makes forge verify --rebuild possible: the tool rebuilds this project and compares
//     the output byte-for-byte against the shipped artifact.
//
// forge plan        — bundle EXE only (requires Engine; see provability.ps1 for details)
// forge plan-diff   — compare two MSI artifacts to see what changed between versions
// forge verify      — rebuild this project and compare output against a shipped artifact
return Installer.Build(args, package =>
{
    package.Name = "Provability Demo";
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

    // Reproducible() is required for forge verify --rebuild to succeed.
    // Without it, each build assigns a fresh random PackageCode GUID and the
    // byte-for-byte comparison will always fail.
    package.Reproducible();

    package.EnableRestartManagerSupport();

    package.Files(files => files
        .Add("payload/readme.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "Provability"));
}, new MsiCompiler());
