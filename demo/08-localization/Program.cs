using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// Multi-language installer with JSON localization files and culture fallback.
//
// Localization files use the naming convention: name.culture.json
//   - strings.en-US.json  (default culture)
//   - strings.de.json     (German)
//   - strings.fr.json     (French)
//
// String references use the !(loc.StringId) syntax in any string property.
// The culture fallback chain resolves: specific -> parent -> default.
//   Example: "de-AT" resolves as de-AT -> de -> en-US

var langDir = Path.Combine(AppContext.BaseDirectory, "lang");

return Installer.Build(args, package =>
{
    // -- Localization setup --------------------------------------------------
    // Load three language files from disk and add one inline culture (de-AT)
    // that demonstrates the fallback chain: de-AT -> de -> en-US.
    package.Localization(loc =>
    {
        loc.AddBuiltInCultures();
        loc.AddJsonFile(Path.Combine(langDir, "strings.en-US.json"));
        loc.AddJsonFile(Path.Combine(langDir, "strings.de.json"));
        loc.AddJsonFile(Path.Combine(langDir, "strings.fr.json"));

        // Austrian German: only override the finish message.
        // All other strings fall back to "de", then "en-US".
        loc.AddCulture("de-AT", new Dictionary<string, string>
        {
            ["FinishMessage"] = "Installation abgeschlossen. Bitte auf Fertigstellen klicken."
        });

        loc.DefaultCulture("en-US");
    });

    // -- Package metadata (using localized references) -----------------------
    package.Name = "!(loc.ProductName)";
    package.Manufacturer = "Falk Software";
    package.Version = new Version(1, 0, 0);
    package.UpgradeCode = new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    package.Description = "!(loc.WelcomeMessage)";
    package.Scope = InstallScope.PerMachine;
    package.Architecture = ProcessorArchitecture.X64;

    package.UseDialogSet(MsiDialogSet.FeatureTree);

    package.DefaultInstallDirectory =
        KnownFolder.ProgramFiles / "Falk Software" / "LocalizedApp";

    // -- Features (localized titles and descriptions) ------------------------
    package.Feature("Core", f =>
    {
        f.Title = "!(loc.FeatureCore)";
        f.Description = "!(loc.FeatureCore)";
        f.IsRequired = true;
        f.IsDefault = true;
    });

    package.Feature("Plugins", f =>
    {
        f.Title = "!(loc.FeaturePlugins)";
        f.Description = "!(loc.FeaturePlugins)";
        f.IsDefault = true;
    });

    // -- Files ---------------------------------------------------------------
    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Falk Software" / "LocalizedApp"));

    package.Files(files => files
        .Add("payload/plugin.dll")
        .To(KnownFolder.ProgramFiles / "Falk Software" / "LocalizedApp" / "Plugins"));

    // -- Shortcuts -----------------------------------------------------------
    package.Shortcut("!(loc.ProductName)", "app.exe")
        .WithDescription("!(loc.WelcomeMessage)")
        .OnStartMenu("Falk Software");

    // -- Major upgrade -------------------------------------------------------
    package.MajorUpgrade(_ => { });
    package.Downgrade(d => d.Block("A newer version is already installed."));
});
