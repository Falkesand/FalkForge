using System.ComponentModel;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class LicensePageViewModel : InstallerPageViewModel, IReactiveObject
{
    private bool _isAccepted;

    public LicensePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        ReactiveNotifications.Enable(this);
    }

    public override string Title => "License Agreement";
    public override string Description => "Please review and accept the license agreement.";

    public string LicenseText => Engine.Manifest.LicenseFile ?? "No license text available.";

    /// <summary>
    /// Whether the user ticked the accept checkbox. Setting it tells the engine, which refuses to
    /// plan a bundle carrying a licence file until it has been told. Ticking the box used to change
    /// nothing but this field, so the install died at the plan with "License agreement has not been
    /// accepted."
    /// </summary>
    public bool IsAccepted
    {
        get => _isAccepted;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAccepted, value);
            Engine.SetLicenseAccepted(value);
        }
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

    public override bool CanNavigateNext()
    {
        return IsAccepted;
    }
}