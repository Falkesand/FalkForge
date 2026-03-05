using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

namespace FalkForge.Ui.ViewModels;

public sealed class WelcomePageViewModel : InstallerPageViewModel
{
    public WelcomePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
    }

    public override string Title => "Welcome";
    public override string Description => $"Welcome to the {Engine.Manifest.Name} installer.";

    public InstallState DetectedState => Engine.DetectedState;

    public bool IsInstalled =>
        DetectedState is InstallState.Installed or InstallState.OlderVersion or InstallState.NewerVersion;

    public bool CanInstall => DetectedState == InstallState.NotInstalled;
    public bool CanUninstall => IsInstalled;
    public bool CanRepair => IsInstalled;

    public override async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        await Engine.DetectAsync(ct);
    }

    public override bool CanNavigateBack()
    {
        return false;
    }
}