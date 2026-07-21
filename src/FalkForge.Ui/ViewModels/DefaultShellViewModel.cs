using System.ComponentModel;
using System.Windows;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Ui.Abstractions;
using FalkForge.Ui.Abstractions.ViewModels;
using ReactiveUI;

namespace FalkForge.Ui.ViewModels;

public sealed class DefaultShellViewModel : InstallerShellViewModel, IReactiveObject
{
    public DefaultShellViewModel(IInstallerEngine engine) : base(engine)
    {
        RegisterPage(new WelcomePageViewModel(engine, this));
        RegisterPage(new LicensePageViewModel(engine, this));
        RegisterPage(new InstallDirPageViewModel(engine, this));
        RegisterPage(new FeaturesPageViewModel(engine, this));
        RegisterPage(new MaintenancePageViewModel(engine, this));
        RegisterPage(new ProgressPageViewModel(engine, this));
        RegisterPage(new CompletePageViewModel(engine, this));

        if (engine is EngineClient engineClient)
        {
            engineClient.UpdateDownloadProgress += (pct, bytes, total) =>
                ForwardUpdateDownloadProgress(pct, bytes, total);
            engineClient.UpdateReady += (version, localPath) =>
                Application.Current.Dispatcher.InvokeAsync(
                    () => HandleUpdateReadyAsync(version, localPath));
        }
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
    ///     Runs detection and navigates to the maintenance page when the product is already installed.
    ///     Call this after construction to determine the initial page.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Engine.DetectAsync(ct);

        // Per-package MSI feature picker: the engine advertises a package's Feature rows during
        // detect (via PackageMsiFeaturesMessage, accumulated on EngineClient). Register the picker
        // page — right after the flat Features page — ONLY when at least one package actually
        // advertised features, so the picker is never shown empty.
        RegisterPackageFeaturePageIfAdvertised();

        if (Engine.DetectedState is InstallState.Installed
            or InstallState.OlderVersion
            or InstallState.NewerVersion)
        {
            IsMaintenanceMode = true;
            await NavigateTo<MaintenancePageViewModel>();
        }
    }

    private void RegisterPackageFeaturePageIfAdvertised()
    {
        if (Engine is not IPackageMsiFeatureChannel channel || channel.PackageMsiFeatures.Count == 0)
            return;

        var insertIndex = FindFeaturesPageIndex() + 1;
        InsertPage(insertIndex, new PackageFeaturesPageViewModel(Engine, this));
    }

    private int FindFeaturesPageIndex()
    {
        for (var i = 0; i < Pages.Count; i++)
            if (Pages[i] is FeaturesPageViewModel)
                return i;

        return Pages.Count - 1;
    }

    internal void ForwardUpdateDownloadProgress(int percent, long bytesReceived, long totalBytes)
    {
        if (CurrentPage is WelcomePageViewModel welcome)
            welcome.UpdateDownloadProgress(percent, bytesReceived, totalBytes);
    }

    internal async Task HandleUpdateReadyAsync(string version, string? localPath)
    {
        var feed = Engine.Manifest.UpdateFeed;
        var shouldPrompt = feed is not null && feed.Policy switch
        {
            UpdatePolicy.DownloadAndPrompt => true,
            UpdatePolicy.AutoUpdate when feed.PromptBeforeAutoUpdate => true,
            _ => false
        };

        if (shouldPrompt)
        {
            var updatePage = new UpdateAvailablePageViewModel(Engine, this);
            updatePage.SetUpdateInfo(version, localPath, 0);
            await InsertPageAfterCurrentAndNavigateAsync(updatePage);
        }
    }

    protected override void OnCurrentPageChanged()
    {
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPage)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoBack)));
        RaisePropertyChanged(new PropertyChangedEventArgs(nameof(CanGoNext)));
    }
}