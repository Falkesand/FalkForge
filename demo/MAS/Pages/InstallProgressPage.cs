using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class InstallProgressPage : MasPageBase<InstallProgressView>
{
    private IDisposable? _progressSubscription;
    private IDisposable? _statusSubscription;
    private string _statusText = string.Empty;
    private int _progressPercent;
    private string _progressDetail = string.Empty;

    public override string Title => Localize("InstallProgress.Title");
    public override bool CanGoBack => false;
    public override bool CanGoNext => true;
    public override bool ShowPreviousButton => false;
    public override string NextButtonText => Localize("Shell.InstallButton");

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        private set => SetField(ref _progressDetail, value);
    }

    public override Task OnNavigatedToAsync()
    {
        StatusText = Localize("InstallProgress.Ready");
        ProgressPercent = 0;
        ProgressDetail = string.Empty;
        return Task.CompletedTask;
    }

    public override PageResult OnNext()
    {
        return PageResult.Install;
    }

    protected override Task<bool> OnApplyBeginAsync()
    {
        StatusText = Localize("InstallProgress.Installing");

        _progressSubscription = Engine.Progress.Subscribe(OnProgress);
        _statusSubscription = Engine.StatusMessage.Subscribe(OnStatusMessage);

        return Task.FromResult(true);
    }

    protected override Task OnApplyCompleteAsync(ApplyResult result)
    {
        _progressSubscription?.Dispose();
        _progressSubscription = null;
        _statusSubscription?.Dispose();
        _statusSubscription = null;

        if (result.ExitCode == 0)
        {
            ProgressPercent = 100;
            StatusText = Localize("InstallProgress.Complete");
            ProgressDetail = string.Empty;
            SharedState.Set("InstallSuccess", true);
        }
        else
        {
            StatusText = Localize("InstallProgress.Failed");
            ProgressDetail = result.ErrorMessage ?? string.Empty;
            SharedState.Set("InstallSuccess", false);
            SharedState.Set("InstallError", result.ErrorMessage ?? string.Empty);
        }

        return Task.CompletedTask;
    }

    public override Task OnNavigatingFromAsync()
    {
        _progressSubscription?.Dispose();
        _progressSubscription = null;
        _statusSubscription?.Dispose();
        _statusSubscription = null;
        return Task.CompletedTask;
    }

    private void OnProgress(InstallProgress progress)
    {
        var percent = progress.Total > 0
            ? (int)(progress.Current * 100.0 / progress.Total)
            : 0;

        ProgressPercent = percent;
        ProgressDetail = progress.CurrentPackage;
    }

    private void OnStatusMessage(string message)
    {
        StatusText = message;
    }
}
