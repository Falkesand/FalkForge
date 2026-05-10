using System.ComponentModel;
using System.Windows.Input;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class CompletePageViewModel : InstallerPageViewModel, IReactiveObject
{
    private bool _isSuccess;
    private bool _launchOnClose;
    private string _message = string.Empty;

    public CompletePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        OpenLogCommand = new RelayCommand(
            () => { LogPathActions.OpenLog(LogPath); return Task.CompletedTask; },
            () => LogPathActions.CanOpen(LogPath));
        OpenLogFolderCommand = new RelayCommand(
            () => { LogPathActions.OpenLogFolder(LogPath); return Task.CompletedTask; },
            () => LogPathActions.CanOpen(LogPath));
    }

    public string? LogPath => Engine.LogPath;

    public bool HasLogPath => !string.IsNullOrWhiteSpace(LogPath);

    public ICommand OpenLogCommand { get; }

    public ICommand OpenLogFolderCommand { get; }

    public override string Title => "Complete";

    public override string Description => IsSuccess
        ? "Installation completed successfully."
        : "Installation failed.";

    public bool IsSuccess
    {
        get => _isSuccess;
        set => this.RaiseAndSetIfChanged(ref _isSuccess, value);
    }

    public string Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public bool LaunchOnClose
    {
        get => _launchOnClose;
        set => this.RaiseAndSetIfChanged(ref _launchOnClose, value);
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
        return false;
    }

    public override bool CanNavigateBack()
    {
        return false;
    }
}