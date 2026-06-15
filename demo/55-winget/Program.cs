using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// WinGet (Windows Package Manager) lets users install apps with a single command:
//   winget install Contoso.MyApp
//
// Publishing to WinGet requires a 3-file YAML manifest set submitted to the
// winget-pkgs repository. FalkForge generates those files automatically at
// compile time, alongside the MSI, with the SHA-256 hash already filled in.

return Installer.Build(args, package =>
{
    package.Name = "My App";
    package.Manufacturer = "Contoso";
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

    // Enable deterministic builds so the SHA-256 hash in the manifest stays
    // stable when the source has not changed.
    package.Reproducible();

    package.Files(files => files
        .Add("payload/app.txt")
        .To(KnownFolder.ProgramFiles / "Contoso" / "MyApp"));

    // Generate the WinGet manifest set alongside the MSI.
    //
    // PackageIdentifier must follow the Publisher.AppName convention used by
    // the winget-pkgs repository. InstallerUrl is the public download URL you
    // will publish — it is embedded in the installer manifest YAML so that
    // winget knows where to fetch the file. License and ShortDescription are
    // required by the WinGet submission schema.
    //
    // After building, three YAML files appear next to the MSI under a
    // directory tree matching the winget-pkgs layout:
    //   c/Contoso/MyApp/1.0.0/Contoso.MyApp.yaml
    //   c/Contoso/MyApp/1.0.0/Contoso.MyApp.installer.yaml
    //   c/Contoso/MyApp/1.0.0/Contoso.MyApp.locale.en-US.yaml
    package.WinGet(w => w
        .PackageIdentifier("Contoso.MyApp")
        .InstallerUrl("https://releases.contoso.com/MyApp/1.0.0/MyApp.msi")
        .License("MIT")
        .ShortDescription("A simple demo application.")
        .Moniker("myapp")
        .Tags("demo", "sample")
        .ReleaseNotesUrl("https://github.com/contoso/myapp/releases/tag/v1.0.0"));

}, new MsiCompiler());
