using FalkForge.Ui;
using CustomUiSimple.Pages;

return InstallerApp.Run(args, app => app
    .Window(w => w
        .Size(500, 350)
        .Title("My App Setup")
        .Accent("#2563EB"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));
