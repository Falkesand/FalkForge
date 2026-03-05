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

    // A second service running under a domain account
    package.Service("DemoWorker", svc =>
    {
        svc.DisplayName = "Demo Worker Service";
        svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
        svc.StartMode = ServiceStartMode.Manual;
        svc.UserName = @".\DemoUser";
        svc.Password = "[DEMO_PASSWORD]";

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
        sc.ServiceName("DemoService");
        sc.StopOnUninstall();
        sc.StartOnInstall();
        sc.DeleteOnUninstall();
        sc.Wait(true);
    });

    // Control an existing service — stop during install, pass arguments on start
    package.ServiceControl(sc =>
    {
        sc.ServiceName("DemoWorker");
        sc.StopOnInstall();
        sc.StartOnInstall();
        sc.Arguments("--config=[INSTALLDIR]config.json");
        sc.DeleteOnUninstall();
    });
}, new MsiCompiler());