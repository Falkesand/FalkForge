using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;

// Demo 09 -- Advanced MSI Features
//
// Showcases the full breadth of the FalkForge MSI API:
//   - Complex feature tree with conditions
//   - File operations: MoveFile, DuplicateFile, RemoveFile, CreateFolder
//   - Service control (start/stop existing services)
//   - Custom actions (SetProperty, deferred DLL from binary)
//   - Custom tables with typed columns and row data
//   - Execute sequence configuration
//   - Media template control
//   - Registry entries and cleanup via RemoveRegistry
//   - Major upgrade with scheduling control
//   - Launch conditions

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, p =>
{
    // ──────────────────────────────────────────────────────────────────
    // Package metadata
    // ──────────────────────────────────────────────────────────────────
    p.Name = "Contoso Server Platform";
    p.Manufacturer = "Contoso Ltd";
    p.Version = new Version(3, 1, 0);
    p.UpgradeCode = new Guid("C0A1B2C3-D4E5-4F60-A7B8-C9D0E1F2A3B4");
    p.Description = "Contoso Server Platform with services, tools, and documentation.";
    p.HelpUrl = "https://contoso.example.com/support";
    p.AboutUrl = "https://contoso.example.com";
    p.Scope = InstallScope.PerMachine;
    p.Architecture = ProcessorArchitecture.X64;

    // Feature tree dialog -- user can select features
    p.UseDialogSet(MsiDialogSet.FeatureTree);

    // Install directory
    var installDir = KnownFolder.ProgramFiles / "Contoso Ltd" / "ServerPlatform";
    var toolsDir = installDir / "Tools";
    var docsDir = installDir / "Documentation";
    var logsDir = KnownFolder.CommonAppData / "Contoso" / "Logs";
    p.DefaultInstallDirectory = installDir;

    // ──────────────────────────────────────────────────────────────────
    // Media template -- control cabinet layout
    // ──────────────────────────────────────────────────────────────────
    p.MediaTemplate(mt => mt
        .CabinetTemplate("contoso{0}.cab")
        .MaxCabinetSizeMB(200)
        .CompressionLevel(FalkForge.CompressionLevel.High)
        .EmbedCabinet(true));

    // ──────────────────────────────────────────────────────────────────
    // Features -- complex tree: Core > (Tools, Documentation)
    // ──────────────────────────────────────────────────────────────────
    p.Feature("Core", core =>
    {
        core.Title = "Core Platform";
        core.Description = "Required server platform binaries.";
        core.IsRequired = true;
        core.IsDefault = true;

        // Sub-feature: Tools (optional)
        core.Feature("Tools", tools =>
        {
            tools.Title = "Administration Tools";
            tools.Description = "CLI and GUI administration utilities.";
            tools.IsDefault = true;

            // Condition: disable on Server Core (no shell)
            tools.Condition(!Condition.Property("MsiNTSuitePersonal"), 0);
        });

        // Sub-feature: Documentation (optional)
        core.Feature("Documentation", docs =>
        {
            docs.Title = "Documentation";
            docs.Description = "User guides and API reference.";
            docs.IsDefault = false;
        });
    });

    // ──────────────────────────────────────────────────────────────────
    // Files -- main application
    // ──────────────────────────────────────────────────────────────────
    p.Files(f => f
        .Add(Path.Combine(payloadDir, "app.exe"))
        .Add(Path.Combine(payloadDir, "app.dll"))
        .To(installDir));

    // Config and docs
    p.Files(f => f
        .Add(Path.Combine(payloadDir, "config.xml"))
        .To(installDir));

    p.Files(f => f
        .Add(Path.Combine(payloadDir, "readme.txt"))
        .To(docsDir));

    // ──────────────────────────────────────────────────────────────────
    // CreateFolder -- ensure log directory exists
    // ──────────────────────────────────────────────────────────────────
    p.CreateFolder(cf => cf
        .Id("CreateLogsFolder")
        .Directory("LOGSDIR"));

    // ──────────────────────────────────────────────────────────────────
    // MoveFile -- move old config on upgrade
    // ──────────────────────────────────────────────────────────────────
    p.MoveFile(mf => mf
        .Id("MoveOldConfig")
        .SourceDirectory("INSTALLFOLDER")
        .SourceFileName("config.old.xml")
        .DestDirectory("INSTALLFOLDER")
        .DestFileName("config.backup.xml")
        .AsMove());

    // ──────────────────────────────────────────────────────────────────
    // DuplicateFile -- copy config as template
    // ──────────────────────────────────────────────────────────────────
    p.DuplicateFile(df => df
        .Id("DuplicateConfigTemplate")
        .FileRef("config.xml")
        .DestDirectory("INSTALLFOLDER")
        .DestFileName("config.template.xml"));

    // ──────────────────────────────────────────────────────────────────
    // RemoveFile -- clean up log files on uninstall
    // ──────────────────────────────────────────────────────────────────
    p.RemoveFile(rf => rf
        .Id("RemoveLogFiles")
        .Directory("LOGSDIR")
        .FileName("*.log")
        .OnUninstall());

    // ──────────────────────────────────────────────────────────────────
    // ServiceControl -- start/stop an existing Windows service
    // ──────────────────────────────────────────────────────────────────
    p.ServiceControl(sc => sc
        .Id("StopContosoAgent")
        .ServiceName("ContosoAgent")
        .StopOnInstall()
        .StopOnUninstall()
        .Wait(true));

    p.ServiceControl(sc => sc
        .Id("StartContosoAgent")
        .ServiceName("ContosoAgent")
        .StartOnInstall()
        .Wait(true));

    // ──────────────────────────────────────────────────────────────────
    // Registry entries -- application configuration
    // ──────────────────────────────────────────────────────────────────
    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Contoso\ServerPlatform", k => k
            .Value("Version", "3.1.0")
            .Value("InstallPath", MsiProperty.InstallFolder)
            .DWord("Installed", 1)
            .Value("LogPath", "[LOGSDIR]", RegistryValueType.ExpandString)));

    p.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Contoso\ServerPlatform\Settings", k => k
            .Value("MaxConnections", "100")
            .Value("ListenPort", "8443")));

    // ──────────────────────────────────────────────────────────────────
    // RemoveRegistry -- clean up on uninstall
    // ──────────────────────────────────────────────────────────────────
    p.RemoveRegistry(rr => rr
        .Id("RemoveContosoRegKey")
        .Root(RegistryRoot.LocalMachine)
        .Key(@"Software\Contoso\ServerPlatform")
        .RemoveKey());

    p.RemoveRegistry(rr => rr
        .Id("RemoveSettingsValue")
        .Root(RegistryRoot.LocalMachine)
        .Key(@"Software\Contoso\ServerPlatform\Settings")
        .Name("MaxConnections")
        .RemoveValue());

    // ──────────────────────────────────────────────────────────────────
    // Custom Actions -- SetProperty type
    // ──────────────────────────────────────────────────────────────────
    p.CustomAction("SetInstallMode", ca =>
    {
        ca.SetProperty("CONTOSO_INSTALL_MODE", "server");
        ca.Condition = Condition.IsInstalling;
        ca.After = "CostFinalize";
    });

    // Custom Action -- SetProperty for upgrade scenario
    p.CustomAction("SetUpgradeFlag", ca =>
    {
        ca.SetProperty("CONTOSO_UPGRADING", "1");
        ca.Condition = Condition.Property("WIX_UPGRADE_DETECTED");
        ca.After = "FindRelatedProducts";
    });

    // Custom Action -- DLL from binary (deferred, elevated)
    p.Binary("ContosoActions", Path.Combine(payloadDir, "app.dll"));
    p.CustomAction("ConfigureDatabase", ca =>
    {
        ca.DllFromBinary("ContosoActions", "ConfigureDatabase");
        ca.Deferred();
        ca.NoImpersonate();
        ca.After = "InstallFiles";
        ca.Condition = Condition.IsInstalling;
    });

    // Custom Action -- Rollback for the deferred action
    p.CustomAction("RollbackDatabase", ca =>
    {
        ca.DllFromBinary("ContosoActions", "RollbackDatabase");
        ca.Rollback();
        ca.NoImpersonate();
        ca.Before = "ConfigureDatabase";
        ca.Condition = Condition.IsInstalling;
    });

    // ──────────────────────────────────────────────────────────────────
    // Custom Table -- track deployment metadata
    // ──────────────────────────────────────────────────────────────────
    p.CustomTable(ct => ct
        .Name("ContosoDeployment")
        .Column("DeploymentId", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Column("Environment", CustomTableColumnType.String, c => c.Width(50))
        .Column("Priority", CustomTableColumnType.Int32)
        .Column("Description", CustomTableColumnType.String, c => c.Nullable().Width(255))
        .Row(r => r
            .Set("DeploymentId", "PROD-001")
            .Set("Environment", "Production")
            .Set("Priority", 1)
            .Set("Description", "Primary production deployment"))
        .Row(r => r
            .Set("DeploymentId", "STG-001")
            .Set("Environment", "Staging")
            .Set("Priority", 2)
            .Set("Description", "Pre-production validation")));

    // Second custom table -- component health checks
    p.CustomTable(ct => ct
        .Name("ContosoHealthCheck")
        .Column("CheckId", CustomTableColumnType.String, c => c.PrimaryKey().Width(72))
        .Column("Endpoint", CustomTableColumnType.String, c => c.Width(255))
        .Column("TimeoutMs", CustomTableColumnType.Int32)
        .Column("Critical", CustomTableColumnType.Int16)
        .Row(r => r
            .Set("CheckId", "HC-API")
            .Set("Endpoint", "/health/api")
            .Set("TimeoutMs", 5000)
            .Set("Critical", (short)1))
        .Row(r => r
            .Set("CheckId", "HC-DB")
            .Set("Endpoint", "/health/database")
            .Set("TimeoutMs", 10000)
            .Set("Critical", (short)1)));

    // ──────────────────────────────────────────────────────────────────
    // Execute Sequence -- schedule custom actions
    // ──────────────────────────────────────────────────────────────────
    p.ExecuteSequence(seq => seq
        .Action("SetInstallMode")
            .After("CostFinalize")
            .Condition(Condition.IsInstalling)
        .Action("SetUpgradeFlag")
            .After("FindRelatedProducts")
            .Condition(Condition.Property("WIX_UPGRADE_DETECTED"))
        .Action("RollbackDatabase")
            .Before("ConfigureDatabase")
            .Condition(Condition.IsInstalling)
        .Action("ConfigureDatabase")
            .After("InstallFiles")
            .Condition(Condition.IsInstalling));

    // ──────────────────────────────────────────────────────────────────
    // Major upgrade -- replace previous versions
    // ──────────────────────────────────────────────────────────────────
    p.MajorUpgrade(mu => mu
        .DowngradeErrorMessage("A newer version of Contoso Server Platform is already installed.")
        .Schedule(RemoveExistingProductsSchedule.AfterInstallInitialize)
        .MigrateFeatures(true));

    // ──────────────────────────────────────────────────────────────────
    // Launch conditions
    // ──────────────────────────────────────────────────────────────────
    p.Require(Condition.IsWindows10OrLater, "Contoso Server Platform requires Windows 10 or later.");
    p.Require("CONTOSO_LICENSE_KEY", "A valid Contoso license key is required. Set the CONTOSO_LICENSE_KEY property.");

    // ──────────────────────────────────────────────────────────────────
    // Additional properties
    // ──────────────────────────────────────────────────────────────────
    p.Property("CONTOSO_LICENSE_KEY", "", prop => prop.IsSecure = true);
    p.Property("ARPNOMODIFY", "1");

}, new MsiCompiler(new WindowsFileSystem()));
