using System.ComponentModel;
using System.Windows.Input;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

namespace FalkForge.Ui.ViewModels;

public sealed class UpdateAvailablePageViewModel : InstallerPageViewModel, INotifyPropertyChanged
{
    private string? _updateVersion;
    private string? _cachedFilePath;
    private long _updateSize;

    public UpdateAvailablePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        UpdateNowCommand = new RelayCommand(() =>
        {
            Engine.LaunchUpdate();
            return Task.CompletedTask;
        });
    }

    public override string Title => "Update Available";

    public override string Description =>
        _updateVersion is not null
            ? $"Version {_updateVersion} of {Engine.Manifest.Name} is available."
            : $"A new version of {Engine.Manifest.Name} is available.";

    public string? UpdateVersion
    {
        get => _updateVersion;
        private set
        {
            if (_updateVersion == value) return;
            _updateVersion = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Description));
        }
    }

    public string? CachedFilePath
    {
        get => _cachedFilePath;
        private set
        {
            if (_cachedFilePath == value) return;
            _cachedFilePath = value;
            OnPropertyChanged();
        }
    }

    public long UpdateSize
    {
        get => _updateSize;
        private set
        {
            if (_updateSize == value) return;
            _updateSize = value;
            OnPropertyChanged();
        }
    }

    public ICommand UpdateNowCommand { get; }

    public void SetUpdateInfo(string version, string? cachedPath, long size)
    {
        UpdateVersion = version;
        CachedFilePath = cachedPath;
        UpdateSize = size;
    }

    public override bool CanNavigateNext() => false;

    public override bool CanNavigateBack() => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
