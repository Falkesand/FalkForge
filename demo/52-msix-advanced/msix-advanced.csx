#r "nuget: FalkForge.Core"
#r "nuget: FalkForge.Compiler.Msix"

using FalkForge;
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;

// MSIX advanced: multiple applications, file type associations, protocol handlers,
// auto-update settings, package dependencies, and visual elements with logos.
InstallerMsix.BuildMsix(Args.ToArray(), msix =>
{
    msix
        .Name("FalkForge.Demo.MsixAdvanced")
        .Publisher("CN=FalkForge Demo")
        .DisplayName("MSIX Advanced Demo")
        .PublisherDisplayName("FalkForge")
        .Version(new Version(2, 0, 0, 0))
        .Architecture(ProcessorArchitecture.X64)
        .Description("Demonstrates advanced MSIX packaging with multiple apps, extensions, and auto-update")
        .LogoPath("assets/StoreLogo.png")
        .MinWindowsVersion("10.0.19041.0")
        .MaxVersionTested("10.0.22621.0")

        // --- Application 1: Main editor application ---
        .Application("Editor", "editor.exe", app => app
            .DisplayName("Demo Editor")
            .Description("Document editor with file type associations")
            .BackgroundColor("#1E1E1E")
            .Square44x44Logo("assets/Square44x44Logo.png")
            .Square150x150Logo("assets/Square150x150Logo.png")
            .Wide310x150Logo("assets/Wide310x150Logo.png"))

        // --- Application 2: CLI utility ---
        .Application("Cli", "demotool.exe", app => app
            .DisplayName("Demo CLI Tool")
            .Description("Command-line utility for document processing")
            .Square44x44Logo("assets/Square44x44Logo.png")
            .Square150x150Logo("assets/Square150x150Logo.png"))

        // --- Application 3: Background service ---
        .Application("SyncService", "sync-service.exe", app => app
            .EntryPoint("DemoApp.SyncService")
            .DisplayName("Demo Sync Service")
            .Description("Background synchronization service")
            .Square44x44Logo("assets/Square44x44Logo.png")
            .Square150x150Logo("assets/Square150x150Logo.png"))

        // --- File type associations ---
        // Register .demo and .dmx file extensions with the Editor application
        .Extension("windows.fileTypeAssociation", "DemoApp.Editor")
        .Extension("windows.fileTypeAssociation", "DemoApp.Editor")

        // --- Protocol handler ---
        // Register demo:// protocol for deep linking
        .Extension("windows.protocol", "DemoApp.Editor")

        // --- Capabilities ---
        .Capability("internetClient")
        .Capability("privateNetworkClientServer")
        .RestrictedCapability("runFullTrust")

        // --- Package dependency ---
        // Declare dependency on the VCLibs framework package
        .Dependency(
            "Microsoft.VCLibs.140.00.UWPDesktop",
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            new Version(14, 0, 30704, 0))

        // --- Auto-update settings ---
        // Check for updates every 6 hours with a user-visible prompt
        .UpdateSettings("https://releases.example.com/demo/Demo.appinstaller", update =>
        {
            update.HoursBetweenUpdateChecks(6);
            update.ShowPrompt();
            update.AutomaticBackgroundTask();
            update.ForceUpdateFromAnyVersion();
        })

        // --- Code signing ---
        .Signing(s => s.CertificatePath("demo-cert.pfx"));
}, (model, outputPath) =>
{
    var compiler = new MsixCompiler();
    return compiler.Compile(model, outputPath);
});
