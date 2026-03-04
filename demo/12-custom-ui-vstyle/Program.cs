using CustomUiVsStyle.Pages;
using FalkForge.Ui;

return InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<ProductPage>("lang.strings.en-US.json")
        .AddJsonResource<ProductPage>("lang.strings.sv-SE.json")
        .DetectCulture()
        .AllowLanguageSelection())
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