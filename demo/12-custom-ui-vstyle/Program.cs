using FalkForge.Ui;
using CustomUiVsStyle.Pages;

return InstallerApp.Run(args, app => app
    .Window(w => w
        .Size(1024, 700)
        .Borderless()
        .Background("#1E1E1E")
        .Accent("#7B68EE")
        .Title("FalkForge DevTools Suite Installer"))
    .Pages(p => p
        .Add<ProductPage>()
        .Add<WorkloadsPage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));
