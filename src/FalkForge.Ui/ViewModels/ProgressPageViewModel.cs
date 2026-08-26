using System.ComponentModel;
using System.Reactive.Linq;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class ProgressPageViewModel : InstallerPageViewModel, IReactiveObject, IDisposable
{
    private string _currentPackage = string.Empty;
    private bool _isComplete;
    private IDisposable? _phaseSubscription;
    private int _progressCurrent;
    private IDisposable? _progressSubscription;
    private int _progressTotal;
    private IDisposable? _statusSubscription;
    private string _statusText = string.Empty;

    public ProgressPageViewModel(IInstallerEngine engine, INavigationService navigation)
        : base(engine, navigation)
    {
        ReactiveNotifications.Enable(this);
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
    {
        PropertyChanging?.Invoke(this, args);
    }

    public void RaisePropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    /// <summary>
    /// The action this page runs when it is shown. Install for the straight-line wizard; the
    /// maintenance page sets Modify, Repair or Uninstall before navigating here.
    /// </summary>
    public InstallAction RequestedAction { get; set; } = InstallAction.Install;

    /// <summary>
    /// Raised once the run has finished, with whether it succeeded and the message to show the
    /// user. The shell forwards it to the completion page, which is the only page that has
    /// anything to say about the outcome.
    /// </summary>
    public event Action<bool, string>? InstallFinished;

    /// <summary>
    /// Runs the install. Showing this page IS the confirmation the wizard collects, so arriving
    /// here plans and then applies.
    /// <para>
    /// Nothing did this before 2026-08-26: the page subscribed to the engine's streams and
    /// returned, and no other page in the built-in wizard called <c>PlanAsync</c> or
    /// <c>ApplyAsync</c> for an install. The bar sat at 0% and the engine never touched a package.
    /// </para>
    /// </summary>
    public override async Task OnNavigatedToAsync(CancellationToken ct = default)
    {
        _progressSubscription = Engine.Progress
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(OnProgress);

        _statusSubscription = Engine.StatusMessage
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(msg => StatusText = msg);

        _phaseSubscription = Engine.Phase
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(OnPhaseChanged);

        await RunAsync(ct);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Engine.PlanAsync(RequestedAction, ct);
            var result = await Engine.ApplyAsync(ct);

            var succeeded = result.ExitCode == 0;
            Finish(succeeded, succeeded
                ? SuccessMessage(RequestedAction)
                : $"The installer stopped with exit code {result.ExitCode}."
                  + (string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : " " + result.ErrorMessage));
        }
        catch (OperationCanceledException)
        {
            Finish(succeeded: false, "The operation was cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // The engine reports a refusal (an unaccepted licence, a missing dependency, a broken
            // pipe) by faulting the request. Say what it said: a progress bar that never moves and
            // never explains itself is the worst thing this page can do.
            Finish(succeeded: false, ex.Message);
        }
    }

    private void Finish(bool succeeded, string message)
    {
        StatusText = message;
        IsComplete = true;
        InstallFinished?.Invoke(succeeded, message);
    }

    private static string SuccessMessage(InstallAction action) => action switch
    {
        InstallAction.Uninstall => "Uninstall completed successfully.",
        InstallAction.Repair => "Repair completed successfully.",
        InstallAction.Modify => "The installation was modified successfully.",
        _ => "Installation completed successfully."
    };

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
        if (phase is EnginePhase.Completing or EnginePhase.Failed) IsComplete = true;
    }

    public override bool CanNavigateNext()
    {
        return IsComplete;
    }

    public override bool CanNavigateBack()
    {
        return false;
    }
}