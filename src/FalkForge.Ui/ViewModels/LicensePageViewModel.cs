namespace FalkForge.Ui.ViewModels;

using System.ComponentModel;
using ReactiveUI;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

public sealed class LicensePageViewModel : InstallerPageViewModel, IReactiveObject
{
    private bool _isAccepted;

    public LicensePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
    }

    public override string Title => "License Agreement";
    public override string Description => "Please review and accept the license agreement.";

    public string LicenseText => Engine.Manifest.LicenseFile ?? "No license text available.";

    public bool IsAccepted
    {
        get => _isAccepted;
        set => this.RaiseAndSetIfChanged(ref _isAccepted, value);
    }

    public override bool CanNavigateNext() => IsAccepted;

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
