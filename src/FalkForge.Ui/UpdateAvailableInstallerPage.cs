using System.Windows;
using System.Windows.Input;

namespace FalkForge.Ui;

/// <summary>
/// InstallerPage wrapper for the update-available prompt.
/// Inserted dynamically by <see cref="ViewModels.CustomShellViewModel"/>
/// when the update policy requires user confirmation.
/// </summary>
internal sealed class UpdateAvailableInstallerPage : InstallerPage
{
    private string? _updateVersion;
    private string? _cachedFilePath;
    private string? _releaseNotes;

    internal Func<Task>? NavigateNextCallback { get; set; }

    public override string Title => "Update Available";

    public string? UpdateVersion
    {
        get => _updateVersion;
        private set => SetField(ref _updateVersion, value);
    }

    public string? CachedFilePath
    {
        get => _cachedFilePath;
        private set => SetField(ref _cachedFilePath, value);
    }

    public string? ReleaseNotes
    {
        get => _releaseNotes;
        private set => SetField(ref _releaseNotes, value);
    }

    public string Description =>
        _updateVersion is not null
            ? $"Version {_updateVersion} of {Engine.Manifest.Name} is available."
            : $"A new version of {Engine.Manifest.Name} is available.";

    public ICommand UpdateNowCommand => new RelayCommand(async () =>
    {
        Engine.LaunchUpdate();
        await Engine.ShutdownAsync();
    });

    public ICommand LaterCommand => new RelayCommand(() =>
    {
        return NavigateNextCallback?.Invoke() ?? Task.CompletedTask;
    });

    public void SetUpdateInfo(string version, string? cachedPath, long size, string? releaseNotes = null)
    {
        UpdateVersion = version;
        CachedFilePath = cachedPath;
        ReleaseNotes = releaseNotes;
        OnPropertyChanged(nameof(Description));
    }

    internal override FrameworkElement CreateViewInternal()
    {
        var view = new Views.UpdateAvailablePage();
        view.DataContext = this;
        return view;
    }
}
