using FalkForge;
using FalkForge.Compiler.Msi;

// Install a Windows service with startup dependencies, failure recovery, and service control.
return Installer.Build(args, package =>
{
    package.Name = "Service Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/myservice.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "ServiceDemo"));

    // Install and configure a Windows service
    package.Service("DemoService", svc =>
    {
        svc.DisplayName = "Demo Background Service";
        svc.Description = "Demonstrates FalkForge service installation";
        svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
        svc.StartMode = ServiceStartMode.Automatic;
        svc.Account = ServiceAccount.LocalService;

        // Pass command-line arguments to the service on startup
        svc.Arguments = "--config=default --log-level=info";

        // Only install this service when the INSTALLSERVICE property is set
        svc.Condition("INSTALLSERVICE ~= \"true\"");

        // Grant the Administrators group full control over the service
        svc.Permission(perm =>
        {
            perm.Domain = "BUILTIN";
            perm.User = "Administrators";
            perm.Permission = 0xF01FF; // SERVICE_ALL_ACCESS
        });

        // Depend on a specific service (won't start until Tcpip is running)
        svc.DependsOn("Tcpip");

        // Depend on a service group (waits for all services in the network group)
        svc.DependsOnGroup("NetworkProvider");

        // Configure failure recovery actions
        svc.FailureActions(fa =>
        {
            fa.OnFirstFailure = FailureAction.Restart;
            fa.OnSecondFailure = FailureAction.Restart;
            fa.OnSubsequentFailures = FailureAction.None;
            fa.ResetPeriod = TimeSpan.FromDays(1);
            fa.RestartDelay = TimeSpan.FromSeconds(30);
        });
    });

    // A second service running under a configurable domain account
    package.Service("DemoWorker", svc =>
    {
        svc.DisplayName = "Demo Worker Service";
        svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
        svc.StartMode = ServiceStartMode.Manual;

        // Use AccountProperty to read the service account from an MSI property at install time.
        // The installer UI or command line sets SERVICEACCOUNT; the engine passes it to the service.
        svc.AccountProperty("[SERVICEACCOUNT]");

        // Service account credentials are passed via MSI properties at install time.
        // In a custom UI installer, use SetSecureProperty() to securely transport
        // the password from the UI to the engine without exposing it on the command line.
        // See demo/14-lifecycle-hooks for the complete secure pattern.
        svc.Password = "[SERVICEPASSWORD]";

        // Run a diagnostic command on failure
        svc.FailureActions(fa =>
        {
            fa.OnFirstFailure = FailureAction.RunCommand;
            fa.Command = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe --diagnose";
            fa.OnSecondFailure = FailureAction.Restart;
            fa.OnSubsequentFailures = FailureAction.Reboot;
            fa.RebootMessage = "Demo Worker service has failed repeatedly. Rebooting.";
        });
    });

    // Service control — stop before uninstall, start after install
    package.ServiceControl(sc =>
    {
        sc.Id("SvcCtrl_DemoService");
        sc.ServiceName("DemoService");
        sc.StopOnUninstall();
        sc.StartOnInstall();
        sc.DeleteOnUninstall();
        sc.Wait(true);
    });

    // Control an existing service — stop during install, pass arguments on start
    package.ServiceControl(sc =>
    {
        sc.Id("SvcCtrl_DemoWorker");
        sc.ServiceName("DemoWorker");
        sc.StopOnInstall();
        sc.StartOnInstall();
        sc.Arguments("--config=[INSTALLDIR]config.json");
        sc.DeleteOnUninstall();
    });
}, new MsiCompiler());