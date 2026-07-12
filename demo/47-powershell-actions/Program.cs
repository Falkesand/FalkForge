using FalkForge;
using FalkForge.Compiler.Msi;

// PowerShell custom actions: inline script, file-based script, deferred execution.
return Installer.Build(args, package =>
{
    package.Name = "PowerShell Actions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "PowerShellDemo"));

    // --- Inline PowerShell script ---
    // Writes an event log entry during installation.
    package.CustomAction("LogInstallStart", ca =>
    {
        ca.PowerShellScript("Write-EventLog -LogName Application -Source 'MSIInstaller' -EventId 1000 -Message 'PowerShell Demo: installation started'");
    });

    // --- File-based PowerShell script ---
    // Reads setup.ps1 and embeds it as an inline PowerShell custom action.
    package.CustomAction("RunSetupScript", ca =>
    {
        ca.PowerShellFile("payload/setup.ps1");
        ca.Deferred();
        ca.NoImpersonate();
    });

    // --- Deferred PowerShell with NoImpersonate ---
    // Runs elevated to configure application settings post-install.
    package.CustomAction("ConfigureSettings", ca =>
    {
        ca.PowerShellScript(@"
$configPath = Join-Path $env:ProgramFiles 'Demo\PowerShellDemo\config.json'
@{ Initialized = $true; InstalledAt = (Get-Date -Format o) } | ConvertTo-Json | Set-Content -Path $configPath -Encoding UTF8
");
        ca.Deferred();
        ca.NoImpersonate();
    });

    // --- Rollback PowerShell action ---
    // Cleans up if the installation fails after ConfigureSettings.
    package.CustomAction("UndoConfigureSettings", ca =>
    {
        ca.PowerShellScript(@"
$configPath = Join-Path $env:ProgramFiles 'Demo\PowerShellDemo\config.json'
if (Test-Path $configPath) { Remove-Item $configPath -Force }
");
        ca.Rollback();
        ca.NoImpersonate();
    });

    // Schedule every custom action above — CustomActionBuilder's After/Before/Condition
    // properties are metadata only; the compiler reads scheduling exclusively from
    // ExecuteSequence(...)/UISequence(...), so each action must be placed here to run.
    package.ExecuteSequence(seq => seq
        .Action("LogInstallStart")
            .After("CostFinalize")
            .Condition(Condition.IsInstalling)
        .Action("RunSetupScript")
            .After("InstallFiles")
        .Action("UndoConfigureSettings")
            .Before("ConfigureSettings")
        .Action("ConfigureSettings")
            .After("RunSetupScript"));
}, new MsiCompiler());
