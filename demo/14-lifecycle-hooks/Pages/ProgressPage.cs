using FalkForge;
using FalkForge.Engine.Protocol;
using FalkForge.Ui;
using FalkForge.Ui.Abstractions;
using LifecycleDemo.Views;

namespace LifecycleDemo.Pages;

public sealed class ProgressPage : InstallerPage<ProgressView>
{
    private string _statusLog = string.Empty;

    public override string Title => "Installing";
    public override bool CanGoBack => false;
    public override bool CanGoNext => true;

    public string StatusLog
    {
        get => _statusLog;
        private set => SetField(ref _statusLog, value);
    }

    private void AddStatus(string message)
    {
        StatusLog += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }

    public override Task OnNavigatedToAsync()
    {
        AddStatus("Ready to install. Click Next to begin.");
        return Task.CompletedTask;
    }

    public override PageResult OnNext()
    {
        return PageResult.Install;
    }

    // -----------------------------------------------
    // LIFECYCLE HOOKS -- This is the teaching example
    // -----------------------------------------------

    protected override Task<bool> OnDetectBeginAsync()
    {
        AddStatus(">> Detect phase starting...");
        return Task.FromResult(true);
    }

    // Per-package granular hook: fires once for each package as detection completes,
    // interleaved between OnDetectBeginAsync and OnDetectCompleteAsync. Observational.
    protected override Task OnDetectPackageCompleteAsync(PackageDetectInfo info)
    {
        var version = info.Version is not null ? $" (installed {info.Version})" : "";
        AddStatus($"   - Detected package '{info.PackageId}': {info.State}{version}");
        return Task.CompletedTask;
    }

    // Per-related-bundle granular hook: fires once per related bundle found on the machine.
    protected override Task OnDetectRelatedBundleAsync(RelatedBundleInfo info)
    {
        AddStatus($"   - Related bundle '{info.BundleId}': {info.Relation} " +
                  $"(installed {info.InstalledVersion})");
        return Task.CompletedTask;
    }

    protected override Task OnDetectCompleteAsync(DetectResult result)
    {
        var description = result.State switch
        {
            InstallState.Installed => "Upgrade from existing installation",
            InstallState.OlderVersion => "Upgrade from older version" +
                                         (result.CurrentVersion is not null ? $" ({result.CurrentVersion})" : ""),
            InstallState.NewerVersion => "Downgrade from newer version" +
                                         (result.CurrentVersion is not null ? $" ({result.CurrentVersion})" : ""),
            _ => "Fresh installation"
        };
        AddStatus($"   Detection complete: {description}");
        return Task.CompletedTask;
    }

    protected override Task<bool> OnPlanBeginAsync(InstallAction action)
    {
        AddStatus($">> Plan phase starting (action: {action})...");

        // Pass collected properties to MSI packages
        var dbServer = SharedState.Get<string>("DbServer") ?? "";
        var dbName = SharedState.Get<string>("DbName") ?? "";
        var integrated = SharedState.Get<bool>("IntegratedSecurity");

        Engine.SetProperty("DBSERVER", dbServer);
        Engine.SetProperty("DBNAME", dbName);
        Engine.SetProperty("INTEGRATEDSECURITY", integrated ? "1" : "0");

        AddStatus($"   Property DBSERVER = {dbServer}");
        AddStatus($"   Property DBNAME = {dbName}");
        AddStatus($"   Property INTEGRATEDSECURITY = {(integrated ? "1" : "0")}");

        if (!integrated)
        {
            var userName = SharedState.Get<string>("DbUserName") ?? "";
            Engine.SetProperty("DBUSERNAME", userName);
            AddStatus($"   Property DBUSERNAME = {userName}");

            // Secure property -- password via named pipe, never on command line
            using var pw = SharedState.GetSensitive("DbPassword");
            if (!pw.IsEmpty)
            {
                Engine.SetSecureProperty("DBPASSWORD", pw);
                AddStatus("   Secure property DBPASSWORD set (pipe transport)");
            }
        }

        return Task.FromResult(true);
    }

    protected override Task OnPlanCompleteAsync(PlanResult result)
    {
        AddStatus($"   Plan complete: {result.PackageActions.Length} package action(s), " +
                  $"{result.TotalDiskSpaceRequired:N0} bytes required");
        return Task.CompletedTask;
    }

    // Per-package granular plan hooks: fire once per package around its planning, interleaved
    // between OnPlanBeginAsync and OnPlanCompleteAsync. Observational.
    protected override Task OnPlanPackageBeginAsync(PackagePlanInfo info)
    {
        AddStatus($"   - Planning '{info.DisplayName}' ({info.PlannedAction})...");
        return Task.CompletedTask;
    }

    protected override Task OnPlanPackageCompleteAsync(PackagePlanInfo info)
    {
        AddStatus($"   - Planned '{info.DisplayName}'");
        return Task.CompletedTask;
    }

    protected override Task<bool> OnApplyBeginAsync()
    {
        AddStatus(">> Apply phase starting...");
        AddStatus("   Pre-flight validation passed");
        return Task.FromResult(true);
    }

    // Per-package granular apply hooks: fire once per package around its execution, interleaved
    // between OnApplyBeginAsync and OnApplyCompleteAsync. Observational.
    protected override Task OnApplyPackageBeginAsync(PackageApplyBeginInfo info)
    {
        AddStatus($"   - Installing '{info.DisplayName}'...");
        return Task.CompletedTask;
    }

    protected override Task OnApplyPackageCompleteAsync(PackageApplyCompleteInfo info)
    {
        AddStatus($"   - {(info.Succeeded ? "Installed" : "FAILED")} '{info.DisplayName}'");
        return Task.CompletedTask;
    }

    protected override Task OnApplyCompleteAsync(ApplyResult result)
    {
        AddStatus(result.ExitCode == 0
            ? "   Apply complete: Installation successful"
            : $"   Apply failed: Exit code {result.ExitCode} - {result.ErrorMessage}");
        return Task.CompletedTask;
    }
}