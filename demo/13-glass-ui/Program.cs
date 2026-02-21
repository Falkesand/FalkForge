using FalkForge.Ui;
using GlassUi.Pages;

return InstallerApp.Run(args, app => app
    .Window(w => w
        .CustomWindow<GlassUi.GlassWindow>()
        .Size(500, 350)
        .Borderless()
        .Title("GlassForge"))
    .Pages(p => p
        .Add<InstallPage>()));
