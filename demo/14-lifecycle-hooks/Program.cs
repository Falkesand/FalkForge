using FalkForge.Ui;
using LifecycleDemo.Pages;

return InstallerApp.Run(args, app => app
    .Window(w => w
        .Size(600, 500)
        .Title("Contoso DataHub Setup")
        .Accent("#0078D4"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ConfigPage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));
