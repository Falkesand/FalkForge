namespace FalkForge.Ui.ViewModels;

using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;

public sealed class ProgressPageViewModel : InstallerPageViewModel, IReactiveObject, IDisposable
{
    private int _progressCurrent;
    private int _progressTotal;
    private string _currentPackage = string.Empty;
    private string _statusText = string.Empty;
    private bool _isComplete;
    private IDisposable? _progressSubscription;
    private IDisposable? _statusSubscription;
    private IDisposable? _phaseSubscription;

    public ProgressPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
    }

    public override string Title => "Installing";
    public override string Description => "Please wait while the installation completes.";

    public int ProgressCurrent
    {
        get => _progressCurrent;
        private set => this.RaiseAndSetIfChanged(ref _progressCurrent, value);
    }

    public int ProgressTotal
    {
        get => _progressTotal;
        private set => this.RaiseAndSetIfChanged(ref _progressTotal, value);
    }

    public string CurrentPackage
    {
        get => _currentPackage;
        private set => this.RaiseAndSetIfChanged(ref _currentPackage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsComplete
    {
        get => _isComplete;
        private set => this.RaiseAndSetIfChanged(ref _isComplete, value);
    }

    public double ProgressPercent =>
        ProgressTotal > 0 ? (double)ProgressCurrent / ProgressTotal * 100.0 : 0.0;

    public override Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        _progressSubscription = Engine.Progress
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnProgress);

        _statusSubscription = Engine.StatusMessage
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(msg => StatusText = msg);

        _phaseSubscription = Engine.Phase
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnPhaseChanged);

        return Task.CompletedTask;
    }

    public override Task OnNavigatingFromAsync(CancellationToken ct = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void OnProgress(InstallProgress progress)
    {
        ProgressCurrent = progress.Current;
        ProgressTotal = progress.Total;
        CurrentPackage = progress.CurrentPackage;
        this.RaisePropertyChanged(nameof(ProgressPercent));
    }

    private void OnPhaseChanged(EnginePhase phase)
    {
        if (phase is EnginePhase.Completing or EnginePhase.Failed)
        {
            IsComplete = true;
        }
    }

    public override bool CanNavigateNext() => IsComplete;
    public override bool CanNavigateBack() => false;

    public void Dispose()
    {
        _progressSubscription?.Dispose();
        _statusSubscription?.Dispose();
        _phaseSubscription?.Dispose();
        _progressSubscription = null;
        _statusSubscription = null;
        _phaseSubscription = null;
    }

    public event PropertyChangingEventHandler? PropertyChanging;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void RaisePropertyChanging(PropertyChangingEventArgs args)
        => PropertyChanging?.Invoke(this, args);

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
        => PropertyChanged?.Invoke(this, args);
}
