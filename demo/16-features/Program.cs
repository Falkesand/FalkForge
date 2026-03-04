using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Nested feature tree: users can select which components to install.
return Installer.Build(args, package =>
{
    package.Name = "Feature Tree Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);
    package.UseDialogSet(MsiDialogSet.FeatureTree);

    package.Feature("Application", app =>
    {
        app.Title = "Application";
        app.Description = "Core application files";
        app.IsRequired = true;

        app.Files(f => f
            .Add("payload/app.exe")
            .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo"));

        // Nested optional feature
        app.Feature("Plugins", plugins =>
        {
            plugins.Title = "Plugins";
            plugins.Description = "Optional editor plugins";
            plugins.IsDefault = true;

            plugins.Files(f => f
                .Add("payload/plugins/editor.dll")
                .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo" / "Plugins"));
        });
    });

    package.Feature("Documentation", docs =>
    {
        docs.Title = "Documentation";
        docs.Description = "User guide and README";
        docs.IsDefault = false;

        docs.Files(f => f
            .Add("payload/docs/readme.txt")
            .To(KnownFolder.ProgramFiles / "Demo" / "FeatureDemo" / "Docs"));
    });

    // Major upgrade — migrate user's feature selections from the old version
    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
        mu.MigrateFeatures(true);
    });
}, new MsiCompiler());