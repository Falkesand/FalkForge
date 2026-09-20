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

    public override bool IsSkippedInLinearFlow =>
        Engine.Manifest.LicenseFile is null && LicenseContent is null;

    public override string Title => "License Agreement";
    public override string Description => "Please review and accept the license agreement.";

    public byte[]? LicenseContent => Engine.Manifest.LicenseContent;

    public string LicenseText
    {
        get
        {
            if (LicenseContent is not { Length: > 0 } content)
                return "No license text available.";

            using var stream = new System.IO.MemoryStream(content, writable: false);
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    private bool _displayFailed;
    public bool CanAccept => !_displayFailed && LicenseContent is { Length: > 0 };

    internal void RejectUnreadableLicense()
    {
        _displayFailed = true;
        IsAccepted = false;
        this.RaisePropertyChanged(nameof(CanAccept));
    }

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
            var accepted = value && CanAccept;
            this.RaiseAndSetIfChanged(ref _isAccepted, accepted);
            Engine.SetLicenseAccepted(accepted);
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
        return IsAccepted && CanAccept;
    }
}