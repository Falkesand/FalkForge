namespace FalkInstaller.Ui.ViewModels;

using System.ComponentModel;
using ReactiveUI;
using FalkInstaller.Ui.Abstractions;
using FalkInstaller.Ui.Abstractions.ViewModels;

public sealed class DefaultShellViewModel : InstallerShellViewModel, IReactiveObject
{
    public DefaultShellViewModel(IInstallerEngine engine) : base(engine)
    {
        RegisterPage(new WelcomePageViewModel(engine, this));
        RegisterPage(new LicensePageViewModel(engine, this));
        RegisterPage(new InstallDirPageViewModel(engine, this));
        RegisterPage(new FeaturesPageViewModel(engine, this));
        RegisterPage(new ProgressPageViewModel(engine, this));
        RegisterPage(new CompletePageViewModel(engine, this));
    }

    protected override void OnCurrentPageChanged()
    {
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPage)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoBack)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoNext)));
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
