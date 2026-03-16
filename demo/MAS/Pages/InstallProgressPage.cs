using FalkForge;
using FalkForge.Engine.Protocol;
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

/// <summary>
/// Displays installation progress and forwards all collected parameters to the engine
/// during the Plan phase. Subscribes to engine progress/status observables during Apply.
/// Matches the WiX BA InstallProgressView.
/// </summary>
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

    protected override Task<bool> OnPlanBeginAsync(InstallAction action)
    {
        if (action is InstallAction.Uninstall)
            return Task.FromResult(true);

        SetStringProperty("DBSERVER", "DatabaseServer");
        SetStringProperty("DBNAME", "DatabaseName");
        SetBoolProperty("INTEGRATEDSECURITY", "IntegratedSecurity");
        SetBoolProperty("TRUSTSERVERCERTIFICATE", "TrustServerCertificate");
        SetStringProperty("DBUSERNAME", "DbUserName");
        SetStringProperty("INSTALLATIONTYPE", "InstallationType");

        SetStringProperty("MSDSNNAME", "MultiServerDsnName");
        SetStringProperty("MSSERVICENAME", "MultiServerServiceName");
        SetStringProperty("MSSERVICEACCOUNT", "MultiServerServiceAccount");
        SetBoolProperty("MSINSTALLASSERVICE", "MultiServerInstallAsService");
        SetStringProperty("MSINSTALLFOLDER", "MultiServerInstallFolder");

        SetStringProperty("MSEXDSNNAME", "MultiServerExDsnName");
        SetStringProperty("MSEXSERVICENAME", "MultiServerExServiceName");
        SetStringProperty("MSEXSERVICEACCOUNT", "MultiServerExServiceAccount");
        SetBoolProperty("MSEXINSTALLASSERVICE", "MultiServerExInstallAsService");
        SetStringProperty("MSEXINSTALLFOLDER", "MultiServerExInstallFolder");

        var useExisting = SharedState.Get<bool>("UseExistingDatabase");
        Engine.SetProperty("INSTALLDB", useExisting ? "false" : "true");
        Engine.SetProperty("ATTACHDATABASE", useExisting ? "true" : "false");

        SetSecureProperty("DBPASSWORD", "DbPassword");
        SetSecureProperty("MSSERVICEPASSWORD", "MultiServerServicePassword");
        SetSecureProperty("MSEXSERVICEPASSWORD", "MultiServerExServicePassword");

        return Task.FromResult(true);
    }

    private void SetStringProperty(string msiProperty, string stateKey)
    {
        var value = SharedState.Get<string>(stateKey);
        if (!string.IsNullOrEmpty(value))
            Engine.SetProperty(msiProperty, value);
    }

    private void SetBoolProperty(string msiProperty, string stateKey)
    {
        var value = SharedState.Get<bool>(stateKey);
        Engine.SetProperty(msiProperty, value ? "1" : "0");
    }

    private void SetSecureProperty(string msiProperty, string stateKey)
    {
        using var sensitive = SharedState.GetSensitive(stateKey);
        if (!sensitive.IsEmpty)
            Engine.SetSecureProperty(msiProperty, sensitive);
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
        var overall = progress.Total > 0
            ? ((progress.Current - 1) * 100 + progress.PackagePercent) / progress.Total
            : 0;

        ProgressPercent = Math.Clamp(overall, 0, 100);

        var locKey = $"InstallProgress.Package.{progress.CurrentPackage}";
        var localized = Localize(locKey);
        ProgressDetail = localized != locKey ? localized : progress.CurrentPackage;
    }

    private void OnStatusMessage(string message)
    {
        StatusText = message;
    }
}
