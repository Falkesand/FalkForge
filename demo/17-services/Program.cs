using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Install a Windows service with startup dependencies and failure recovery.
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

        // Depend on another service (won't start until Tcpip is running)
        svc.DependsOn("Tcpip");
    });

    // Stop the service before uninstall, start after install
    package.ServiceControl(sc =>
    {
        sc.ServiceName("DemoService");
        sc.StopOnUninstall();
        sc.StartOnInstall();
        sc.DeleteOnUninstall();
    });

}, new MsiCompiler());
