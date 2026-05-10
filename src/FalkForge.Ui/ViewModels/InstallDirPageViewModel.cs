using System.ComponentModel;
using System.IO;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class InstallDirPageViewModel : InstallerPageViewModel, IReactiveObject
{
    /// <summary>
    /// Minimum free space required before allowing navigation (100 MB).
    /// </summary>
    private const long MinFreeSpaceBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Maximum path length enforced when the OS long-paths registry key is disabled.
    /// 240 chars leaves room for nested files created inside the chosen directory.
    /// </summary>
    private const int MaxPathLengthWithoutLongPaths = 240;

    private string _installDirectory = string.Empty;
    private string? _validationError;

    public InstallDirPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        _installDirectory = engine.InstallDirectory;
    }

    public override string Title => "Installation Directory";
    public override string Description => "Choose where to install the application.";

    /// <summary>
    /// Injectable drive-info provider; defaults to the real OS implementation.
    /// Replace in tests to avoid filesystem/registry access.
    /// </summary>
    public IDriveInfoProvider DriveInfoProvider { get; set; } = FalkForge.Ui.DriveInfoProvider.Default;

    public string InstallDirectory
    {
        get => _installDirectory;
        set
        {
            this.RaiseAndSetIfChanged(ref _installDirectory, value);
            Engine.InstallDirectory = value;
        }
    }

    /// <summary>
    /// Inline validation message; null when the path is valid.
    /// </summary>
    public string? ValidationError
    {
        get => _validationError;
        private set => this.RaiseAndSetIfChanged(ref _validationError, value);
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
        if (string.IsNullOrWhiteSpace(InstallDirectory))
        {
            ValidationError = null;
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(InstallDirectory);
            if (!Path.IsPathFullyQualified(fullPath))
            {
                ValidationError = null;
                return false;
            }
        }
        catch
        {
            ValidationError = null;
            return false;
        }

        // Path length guard: when the OS long-paths feature is disabled, Windows
        // enforces MAX_PATH (260). We cap at 240 to leave headroom for nested files.
        if (!DriveInfoProvider.IsLongPathsEnabled() &&
            fullPath.Length > MaxPathLengthWithoutLongPaths)
        {
            ValidationError = $"The path is too long ({{fullPath.Length}} characters). " +
                              $"Keep it under {{MaxPathLengthWithoutLongPaths}} characters, " +
                              "or enable long path support in Windows settings.";
            return false;
        }

        // Free-space guard.
        var freeSpace = DriveInfoProvider.GetAvailableFreeSpace(fullPath);
        if (freeSpace < MinFreeSpaceBytes)
        {
            ValidationError = $"Insufficient free disk space. " +
                              $"At least {{MinFreeSpaceBytes / (1024 * 1024)}} MB is required; " +
                              $"{{freeSpace / (1024 * 1024)}} MB available.";
            return false;
        }

        // Writable probe.
        if (!DriveInfoProvider.IsWritable(fullPath))
        {
            ValidationError = "The selected directory is not writable. " +
                              "Choose a different location or run as administrator.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_installDirectory)) InstallDirectory = Engine.InstallDirectory;

        return Task.CompletedTask;
    }
}
