using CustomUiSimple.Pages;
using FalkForge.Ui;

return InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResources()
        .DetectCulture()
        .AllowLanguageSelection())
    .Window(w => w
        .Size(500, 350)
        .Title("My App Setup")
        .Accent("#2563EB"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));