using System.ComponentModel;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class DefaultShellViewModel : InstallerShellViewModel, IReactiveObject
{
    public DefaultShellViewModel(IInstallerEngine engine) : base(engine)
    {
        RegisterPage(new WelcomePageViewModel(engine, this));
        RegisterPage(new LicensePageViewModel(engine, this));
        RegisterPage(new InstallDirPageViewModel(engine, this));
        RegisterPage(new FeaturesPageViewModel(engine, this));
        RegisterPage(new MaintenancePageViewModel(engine, this));
        RegisterPage(new ProgressPageViewModel(engine, this));
        RegisterPage(new CompletePageViewModel(engine, this));
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
    {
        PropertyChanging?.Invoke(this, args);
    }

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    /// <summary>
    ///     Runs detection and navigates to the maintenance page when the product is already installed.
    ///     Call this after construction to determine the initial page.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Engine.DetectAsync(ct);

        if (Engine.DetectedState is InstallState.Installed
            or InstallState.OlderVersion
            or InstallState.NewerVersion)
        {
            IsMaintenanceMode = true;
            NavigateTo<MaintenancePageViewModel>();
        }
    }

    protected override void OnCurrentPageChanged()
    {
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPage)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoBack)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoNext)));
    }
}