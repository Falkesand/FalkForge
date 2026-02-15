namespace FalkForge.Ui.ViewModels;

using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ReactiveUI;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

public sealed class MaintenancePageViewModel : InstallerPageViewModel, IReactiveObject, IDisposable
{
    private bool _isOperationInProgress;
    private string? _errorMessage;
    private readonly CompositeDisposable _disposables = new();

    public MaintenancePageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        var canExecute = this.WhenAnyValue(x => x.IsOperationInProgress, inProgress => !inProgress);

        ModifyCommand = ReactiveCommand.CreateFromTask(ExecuteModifyAsync, canExecute);
        RepairCommand = ReactiveCommand.CreateFromTask(ExecuteRepairAsync, canExecute);
        UninstallCommand = ReactiveCommand.CreateFromTask(ExecuteUninstallAsync, canExecute);

        ModifyCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = ex.Message);
        RepairCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = ex.Message);
        UninstallCommand.ThrownExceptions.Subscribe(ex => ErrorMessage = ex.Message);

        _disposables.Add(ModifyCommand);
        _disposables.Add(RepairCommand);
        _disposables.Add(UninstallCommand);
    }

    public override string Title => "Modify Setup";
    public override string Description => $"{ProductName} is already installed. Choose an option below.";

    public string ProductName => Engine.Manifest.Name;

    public string InstalledVersion
    {
        get
        {
            var state = Engine.DetectedState;
            if (state is InstallState.Installed or InstallState.OlderVersion or InstallState.NewerVersion)
                return Engine.Manifest.Version;
            return string.Empty;
        }
    }

    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        private set => this.RaiseAndSetIfChanged(ref _isOperationInProgress, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ModifyCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RepairCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> UninstallCommand { get; }

    public override bool CanNavigateNext() => false;
    public override bool CanNavigateBack() => false;

    private async Task ExecuteModifyAsync(CancellationToken ct)
    {
        await PlanAndNavigateAsync(InstallAction.Modify, ct);
    }

    private async Task ExecuteRepairAsync(CancellationToken ct)
    {
        await PlanAndNavigateAsync(InstallAction.Repair, ct);
    }

    private async Task ExecuteUninstallAsync(CancellationToken ct)
    {
        await PlanAndNavigateAsync(InstallAction.Uninstall, ct);
    }

    private async Task PlanAndNavigateAsync(InstallAction action, CancellationToken ct)
    {
        IsOperationInProgress = true;
        try
        {
            await Engine.PlanAsync(action, ct);
            Navigation.NavigateTo<ProgressPageViewModel>();
        }
        finally
        {
            IsOperationInProgress = false;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
