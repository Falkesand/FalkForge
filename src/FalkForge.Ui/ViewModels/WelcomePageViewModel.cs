using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

namespace FalkForge.Ui.ViewModels;

public sealed class WelcomePageViewModel : InstallerPageViewModel, INotifyPropertyChanged
{
    private int _downloadPercent;
    private bool _isDownloadingUpdate;

    public WelcomePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        InstallCommand = new RelayCommand(StartInstallAsync, () => CanInstall);
    }

    /// <summary>
    /// Drives the Install button on the welcome page. The button bound Content, Style, Width and
    /// Visibility and nothing else, so pressing it did nothing whatsoever.
    /// </summary>
    public ICommand InstallCommand { get; }

    /// <summary>
    /// Starts the install by moving on to the next page of the wizard, which is what the Install
    /// button and the Next button both mean here.
    /// </summary>
    internal Task StartInstallAsync() => Navigation.NavigateNext();

    public override string Title => "Welcome";
    public override string Description => $"Welcome to the {Engine.Manifest.Name} installer.";

    public InstallState DetectedState => Engine.DetectedState;

    public bool IsInstalled =>
        DetectedState is InstallState.Installed or InstallState.OlderVersion or InstallState.NewerVersion;

    public bool CanInstall => DetectedState == InstallState.NotInstalled;
    public bool CanUninstall => IsInstalled;
    public bool CanRepair => IsInstalled;

    public int DownloadPercent
    {
        get => _downloadPercent;
        private set
        {
            if (_downloadPercent == value) return;
            _downloadPercent = value;
            OnPropertyChanged();
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (_isDownloadingUpdate == value) return;
            _isDownloadingUpdate = value;
            OnPropertyChanged();
        }
    }

    public void UpdateDownloadProgress(int percent, long bytesReceived, long totalBytes)
    {
        DownloadPercent = percent;
        IsDownloadingUpdate = percent < 100;
    }

    public override async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        await Engine.DetectAsync(ct);
    }

    public override bool CanNavigateBack()
    {
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}