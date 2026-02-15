namespace FalkForge.Ui.ViewModels;

using System.ComponentModel;
using System.IO;
using ReactiveUI;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

public sealed class InstallDirPageViewModel : InstallerPageViewModel, IReactiveObject
{
    private string _installDirectory = string.Empty;

    public InstallDirPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        _installDirectory = engine.InstallDirectory;
    }

    public override string Title => "Installation Directory";
    public override string Description => "Choose where to install the application.";

    public string InstallDirectory
    {
        get => _installDirectory;
        set
        {
            this.RaiseAndSetIfChanged(ref _installDirectory, value);
            Engine.InstallDirectory = value;
        }
    }

    public override bool CanNavigateNext()
    {
        if (string.IsNullOrWhiteSpace(InstallDirectory))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(InstallDirectory);
            return Path.IsPathFullyQualified(fullPath);
        }
        catch
        {
            return false;
        }
    }

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_installDirectory))
        {
            InstallDirectory = Engine.InstallDirectory;
        }

        return Task.CompletedTask;
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
