using FalkForge;
using FalkForge.Compiler.Msi;

// ICE validation: enable validation, suppress rules, warnings as errors, JSON report.
return Installer.Build(args, package =>
{
    package.Name = "ICE Validation Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "IceValidationDemo"));

    // --- ICE validation configuration ---
    // Enables MSI Internal Consistency Evaluators (ICE) during compilation.
    package.Ice(ice =>
    {
        // Suppress specific ICE rules that don't apply to this package:
        //   ICE61: Checks for valid upgrade code (not needed for demo packages)
        //   ICE91: Checks for missing file hash info (placeholder files have no hash)
        ice.Suppress("ICE61", "ICE91");

        // Treat all ICE warnings as errors — ensures strict validation compliance.
        ice.WarningsAsErrors();

        // Write validation results to a JSON report file for CI/CD integration.
        ice.ReportPath("output/ice-report.json");
    });

    // Add a major upgrade entry so the package is production-like
    package.MajorUpgrade(mu => mu
        .DowngradeMessage("A newer version is already installed."));
}, new MsiCompiler());
