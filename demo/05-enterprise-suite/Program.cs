using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

var payloadDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

return Installer.Build(args, pkg =>
{
    // ──────────────────────────────────────────────────────────────────
    // Package metadata
    // ──────────────────────────────────────────────────────────────────
    pkg.Name = "Apex Enterprise Development Suite";
    pkg.Manufacturer = "Apex Software Inc.";
    pkg.Version = new Version(25, 1, 0);
    pkg.Description = "Complete enterprise development environment";
    pkg.Scope = InstallScope.PerMachine;
    pkg.Architecture = ProcessorArchitecture.X64;
    pkg.LicenseFile = Path.Combine(payloadDir, "license.rtf");
    pkg.HelpUrl = "https://support.apexsoftware.com";
    pkg.AboutUrl = "https://www.apexsoftware.com/enterprise-suite";
    pkg.UpdateUrl = "https://www.apexsoftware.com/updates";

    pkg.UseDialogSet(MsiDialogSet.Advanced);
    pkg.EnableRestartManagerSupport();
    pkg.CabinetThreads(4);

    // ──────────────────────────────────────────────────────────────────
    // Install directories
    // ──────────────────────────────────────────────────────────────────
    var installDir = KnownFolder.ProgramFiles / "Apex Software Inc." / "Enterprise Suite";
    pkg.DefaultInstallDirectory = installDir;

    var ideDir = installDir / "IDE";
    var webServerDir = installDir / "Web" / "Server";
    var webFrameworksDir = installDir / "Web" / "Frameworks";
    var webBrowserDir = installDir / "Web" / "BrowserTools";
    var dbExplorerDir = installDir / "Database" / "Explorer";
    var dbSchemaDir = installDir / "Database" / "Schema";
    var dbProfilerDir = installDir / "Database" / "Profiler";
    var mobileDir = installDir / "Mobile";
    var cloudDir = installDir / "Cloud";
    var collabDir = installDir / "Collaboration";
    var docsApiDir = installDir / "Docs" / "API";
    var docsTutorialsDir = installDir / "Docs" / "Tutorials";
    var docsSamplesDir = installDir / "Docs" / "Samples";
    var diagnosticsDir = installDir / "Diagnostics";
    var fontsDir = KnownFolder.FontsFolder;

    // ──────────────────────────────────────────────────────────────────
    // Feature 1: IDE (required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("IDE", ide =>
    {
        ide.Title = "Apex IDE";
        ide.Description = "Core integrated development environment";
        ide.IsRequired = true;
        ide.IsDefault = true;

        ide.Files(f => f
            .Add(Path.Combine(payloadDir, "ide", "apex.exe"))
            .Add(Path.Combine(payloadDir, "ide", "apex.core.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.ui.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.editor.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.project.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.debugger.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.intellisense.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.themes.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.extensions.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.settings.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.telemetry.dll"))
            .Add(Path.Combine(payloadDir, "ide", "apex.updater.dll"))
            .Add(Path.Combine(payloadDir, "ide", "splash.png"))
            .Add(Path.Combine(payloadDir, "ide", "default-theme.json"))
            .Add(Path.Combine(payloadDir, "ide", "keybindings.json"))
            .To(ideDir));
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 2: Web Development Tools (default, not required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("WebTools", web =>
    {
        web.Title = "Web Development Tools";
        web.Description = "Tools for building modern web applications";
        web.IsRequired = false;
        web.IsDefault = true;

        // Nested: Web Server
        web.Feature("WebServer", ws =>
        {
            ws.Title = "Web Server";
            ws.Description = "Built-in development web server";
            ws.IsDefault = true;

            ws.Files(f => f
                .Add(Path.Combine(payloadDir, "web", "server", "webserver.exe"))
                .Add(Path.Combine(payloadDir, "web", "server", "webserver.core.dll"))
                .Add(Path.Combine(payloadDir, "web", "server", "webserver.http.dll"))
                .Add(Path.Combine(payloadDir, "web", "server", "webserver.ssl.dll"))
                .Add(Path.Combine(payloadDir, "web", "server", "web.config"))
                .To(webServerDir));
        });

        // Nested: Web Frameworks
        web.Feature("WebFrameworks", wf =>
        {
            wf.Title = "Web Frameworks";
            wf.Description = "MVC, API, and SPA frameworks";
            wf.IsDefault = true;

            wf.Files(f => f
                .Add(Path.Combine(payloadDir, "web", "frameworks", "framework.mvc.dll"))
                .Add(Path.Combine(payloadDir, "web", "frameworks", "framework.api.dll"))
                .Add(Path.Combine(payloadDir, "web", "frameworks", "framework.spa.dll"))
                .Add(Path.Combine(payloadDir, "web", "frameworks", "templates.dll"))
                .Add(Path.Combine(payloadDir, "web", "frameworks", "scaffold.exe"))
                .To(webFrameworksDir));
        });

        // Nested: Browser Tools
        web.Feature("BrowserTools", bt =>
        {
            bt.Title = "Browser Tools";
            bt.Description = "Chrome and Firefox developer tools integration";
            bt.IsDefault = true;

            bt.Files(f => f
                .Add(Path.Combine(payloadDir, "web", "browser", "devtools.dll"))
                .Add(Path.Combine(payloadDir, "web", "browser", "devtools.chrome.dll"))
                .Add(Path.Combine(payloadDir, "web", "browser", "devtools.firefox.dll"))
                .To(webBrowserDir));
        });
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 3: Database Tools (default, not required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("DatabaseTools", db =>
    {
        db.Title = "Database Tools";
        db.Description = "SQL database development and administration";
        db.IsRequired = false;
        db.IsDefault = true;

        // Nested: SQL Explorer
        db.Feature("SqlExplorer", se =>
        {
            se.Title = "SQL Explorer";
            se.Description = "Browse and query SQL databases";
            se.IsDefault = true;

            se.Files(f => f
                .Add(Path.Combine(payloadDir, "database", "explorer", "sqlexplorer.exe"))
                .Add(Path.Combine(payloadDir, "database", "explorer", "sqlexplorer.core.dll"))
                .Add(Path.Combine(payloadDir, "database", "explorer", "sqlexplorer.ui.dll"))
                .Add(Path.Combine(payloadDir, "database", "explorer", "sqlexplorer.intellisense.dll"))
                .Add(Path.Combine(payloadDir, "database", "explorer", "drivers.dll"))
                .To(dbExplorerDir));
        });

        // Nested: Schema Designer
        db.Feature("SchemaDesigner", sd =>
        {
            sd.Title = "Schema Designer";
            sd.Description = "Visual database schema design and migrations";
            sd.IsDefault = true;

            sd.Files(f => f
                .Add(Path.Combine(payloadDir, "database", "schema", "schema.dll"))
                .Add(Path.Combine(payloadDir, "database", "schema", "schema.visual.dll"))
                .Add(Path.Combine(payloadDir, "database", "schema", "migrations.dll"))
                .To(dbSchemaDir));
        });

        // Nested: Performance Profiler (opt-in)
        db.Feature("DbProfiler", dp =>
        {
            dp.Title = "Performance Profiler";
            dp.Description = "Database query performance analysis";
            dp.IsDefault = false;

            dp.Files(f => f
                .Add(Path.Combine(payloadDir, "database", "profiler", "profiler.exe"))
                .Add(Path.Combine(payloadDir, "database", "profiler", "profiler.core.dll"))
                .Add(Path.Combine(payloadDir, "database", "profiler", "profiler.ui.dll"))
                .To(dbProfilerDir));
        });
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 4: Mobile SDK (opt-in)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("MobileSDK", mobile =>
    {
        mobile.Title = "Mobile SDK";
        mobile.Description = "Cross-platform mobile development tools";
        mobile.IsRequired = false;
        mobile.IsDefault = false;

        mobile.Files(f => f
            .Add(Path.Combine(payloadDir, "mobile", "mobile.sdk.dll"))
            .Add(Path.Combine(payloadDir, "mobile", "mobile.emulator.exe"))
            .Add(Path.Combine(payloadDir, "mobile", "mobile.designer.dll"))
            .Add(Path.Combine(payloadDir, "mobile", "android.tools.dll"))
            .Add(Path.Combine(payloadDir, "mobile", "ios.bridge.dll"))
            .To(mobileDir));
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 5: Cloud Tools (default, not required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("CloudTools", cloud =>
    {
        cloud.Title = "Cloud Tools";
        cloud.Description = "Cloud deployment, monitoring, and CLI";
        cloud.IsRequired = false;
        cloud.IsDefault = true;

        cloud.Files(f => f
            .Add(Path.Combine(payloadDir, "cloud", "cloud.cli.exe"))
            .Add(Path.Combine(payloadDir, "cloud", "cloud.core.dll"))
            .Add(Path.Combine(payloadDir, "cloud", "cloud.deploy.dll"))
            .Add(Path.Combine(payloadDir, "cloud", "cloud.monitor.dll"))
            .Add(Path.Combine(payloadDir, "cloud", "cloud.config.json"))
            .To(cloudDir));
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 6: Collaboration Tools (default, not required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("Collaboration", collab =>
    {
        collab.Title = "Collaboration Tools";
        collab.Description = "Git integration, code review, and pair programming";
        collab.IsRequired = false;
        collab.IsDefault = true;

        collab.Files(f => f
            .Add(Path.Combine(payloadDir, "collab", "git.integration.dll"))
            .Add(Path.Combine(payloadDir, "collab", "code-review.dll"))
            .Add(Path.Combine(payloadDir, "collab", "pair-programming.dll"))
            .Add(Path.Combine(payloadDir, "collab", "team.dll"))
            .To(collabDir));
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 7: Documentation & Samples (default, not required)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("Documentation", docs =>
    {
        docs.Title = "Documentation & Samples";
        docs.Description = "API reference, tutorials, and sample projects";
        docs.IsRequired = false;
        docs.IsDefault = true;

        // Nested: API Reference
        docs.Feature("ApiReference", api =>
        {
            api.Title = "API Reference";
            api.Description = "Complete API documentation with search";
            api.IsDefault = true;

            api.Files(f => f
                .Add(Path.Combine(payloadDir, "docs", "api", "api-reference.html"))
                .Add(Path.Combine(payloadDir, "docs", "api", "api-index.json"))
                .Add(Path.Combine(payloadDir, "docs", "api", "search.js"))
                .To(docsApiDir));
        });

        // Nested: Tutorials
        docs.Feature("Tutorials", tut =>
        {
            tut.Title = "Tutorials";
            tut.Description = "Step-by-step guides for web, mobile, and cloud";
            tut.IsDefault = true;

            tut.Files(f => f
                .Add(Path.Combine(payloadDir, "docs", "tutorials", "tutorial-web.html"))
                .Add(Path.Combine(payloadDir, "docs", "tutorials", "tutorial-mobile.html"))
                .Add(Path.Combine(payloadDir, "docs", "tutorials", "tutorial-cloud.html"))
                .To(docsTutorialsDir));
        });

        // Nested: Sample Projects (opt-in)
        docs.Feature("Samples", samples =>
        {
            samples.Title = "Sample Projects";
            samples.Description = "Ready-to-build sample applications";
            samples.IsDefault = false;

            samples.Files(f => f
                .Add(Path.Combine(payloadDir, "docs", "samples", "sample-webapp.zip"))
                .Add(Path.Combine(payloadDir, "docs", "samples", "sample-api.zip"))
                .Add(Path.Combine(payloadDir, "docs", "samples", "sample-mobile.zip"))
                .Add(Path.Combine(payloadDir, "docs", "samples", "sample-cloud.zip"))
                .Add(Path.Combine(payloadDir, "docs", "samples", "sample-fullstack.zip"))
                .To(docsSamplesDir));
        });
    });

    // ──────────────────────────────────────────────────────────────────
    // Feature 8: Diagnostics & Profiling (opt-in)
    // ──────────────────────────────────────────────────────────────────
    pkg.Feature("Diagnostics", diag =>
    {
        diag.Title = "Diagnostics & Profiling";
        diag.Description = "CPU, memory, and network profiling tools";
        diag.IsRequired = false;
        diag.IsDefault = false;

        diag.Files(f => f
            .Add(Path.Combine(payloadDir, "diagnostics", "diagnostics.exe"))
            .Add(Path.Combine(payloadDir, "diagnostics", "diagnostics.core.dll"))
            .Add(Path.Combine(payloadDir, "diagnostics", "cpu-profiler.dll"))
            .Add(Path.Combine(payloadDir, "diagnostics", "memory-profiler.dll"))
            .Add(Path.Combine(payloadDir, "diagnostics", "network-analyzer.dll"))
            .To(diagnosticsDir));
    });

    // ──────────────────────────────────────────────────────────────────
    // Shortcuts
    // ──────────────────────────────────────────────────────────────────

    // Desktop shortcut: Apex IDE
    pkg.Shortcut("Apex IDE", "[INSTALLFOLDER]IDE\\apex.exe")
        .WithDescription("Launch Apex Enterprise IDE")
        .OnDesktop();

    // Start Menu shortcuts under "Apex Enterprise Suite"
    pkg.Shortcut("Apex IDE", "[INSTALLFOLDER]IDE\\apex.exe")
        .WithDescription("Launch Apex Enterprise IDE")
        .OnStartMenu("Apex Enterprise Suite");

    pkg.Shortcut("Apex Command Prompt", "[INSTALLFOLDER]IDE\\apex.exe")
        .WithArguments("--terminal")
        .WithDescription("Open Apex command-line environment")
        .OnStartMenu("Apex Enterprise Suite");

    // SQL Explorer start menu shortcut
    pkg.Shortcut("SQL Explorer", "[INSTALLFOLDER]Database\\Explorer\\sqlexplorer.exe")
        .WithDescription("Browse and query SQL databases")
        .OnStartMenu("Apex Enterprise Suite");

    // Diagnostics start menu shortcut
    pkg.Shortcut("Apex Diagnostics", "[INSTALLFOLDER]Diagnostics\\diagnostics.exe")
        .WithDescription("CPU, memory, and network profiling")
        .OnStartMenu("Apex Enterprise Suite");

    // ──────────────────────────────────────────────────────────────────
    // Service: ApexWebServer
    // ──────────────────────────────────────────────────────────────────
    pkg.Service("ApexWebServer", svc =>
    {
        svc.DisplayName = "Apex Development Web Server";
        svc.Description = "Local development web server for Apex IDE";
        svc.Executable = "[INSTALLFOLDER]Web\\Server\\webserver.exe";
        svc.StartMode = ServiceStartMode.Manual;
        svc.Account = ServiceAccount.LocalService;
    });

    // ──────────────────────────────────────────────────────────────────
    // Registry entries
    // ──────────────────────────────────────────────────────────────────
    pkg.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\ApexSoftware\EnterpriseSuite", k => k
            .Value("ProductVersion", "2025.1.0")
            .Value("InstallPath", "[INSTALLFOLDER]")
            .Value("EditorFont", "ApexMono")));

    pkg.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\ApexSoftware\MobileSDK", k => k
            .Value("SdkPath", "[INSTALLFOLDER]Mobile")));

    pkg.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\ApexSoftware\CloudTools", k => k
            .Value("CliPath", "[INSTALLFOLDER]Cloud")));

    // ──────────────────────────────────────────────────────────────────
    // File associations
    // ──────────────────────────────────────────────────────────────────
    pkg.FileAssociation(".aproj", fa =>
    {
        fa.ProgId("ApexSoftware.ApexProject");
        fa.Description = "Apex Project";
        fa.Verb(ShellVerb.Open, "\"%1\"");
    });

    pkg.FileAssociation(".asln", fa =>
    {
        fa.ProgId("ApexSoftware.ApexSolution");
        fa.Description = "Apex Solution";
        fa.Verb(ShellVerb.Open, "\"%1\"");
    });

    // ──────────────────────────────────────────────────────────────────
    // Environment variables
    // ──────────────────────────────────────────────────────────────────
    pkg.EnvironmentVariable("APEX_HOME", "[INSTALLFOLDER]", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Set;
    });

    pkg.EnvironmentVariable("PATH", "[INSTALLFOLDER]bin", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Append;
        env.Separator = ";";
    });

    pkg.EnvironmentVariable("APEX_MOBILE_SDK", "[INSTALLFOLDER]Mobile", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Set;
    });

    pkg.EnvironmentVariable("PATH", "[INSTALLFOLDER]Cloud", env =>
    {
        env.IsSystem = true;
        env.Action = EnvironmentVariableAction.Append;
        env.Separator = ";";
    });

    // ──────────────────────────────────────────────────────────────────
    // Custom action: SetProperty for APEX_VERSION
    // CustomActionBuilder's After/Before/Condition properties are metadata only — the
    // compiler reads scheduling exclusively from ExecuteSequence(...)/UISequence(...), so
    // the action is placed there below to actually run.
    // ──────────────────────────────────────────────────────────────────
    pkg.CustomAction("SetApexVersion", ca =>
    {
        ca.SetProperty("APEX_VERSION", "2025.1.0");
    });

    pkg.ExecuteSequence(seq => seq
        .Action("SetApexVersion")
        .After("CostFinalize"));

    // ──────────────────────────────────────────────────────────────────
    // Major upgrade
    // ──────────────────────────────────────────────────────────────────
    pkg.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallValidate);
    });
    pkg.Downgrade(d => d.Block(
        "A newer version of Apex Enterprise Suite is already installed. Please uninstall it first."));

    // ──────────────────────────────────────────────────────────────────
    // Launch conditions
    // ──────────────────────────────────────────────────────────────────
    pkg.Require("Privileged", "Administrator privileges are required to install Apex Enterprise Suite.");
    pkg.Require("VersionNT64", "Apex Enterprise Suite requires a 64-bit operating system.");

    // ──────────────────────────────────────────────────────────────────
    // Custom table: ApexComponents
    // ──────────────────────────────────────────────────────────────────
    pkg.CustomTable(ct =>
    {
        ct.Name("ApexComponents");
        ct.Column("ComponentId", CustomTableColumnType.String, c => c.PrimaryKey().Width(72));
        ct.Column("Category", CustomTableColumnType.String, c => c.Width(64));
        ct.Column("Priority", CustomTableColumnType.Int32);

        ct.Row(r => r
            .Set("ComponentId", "apex.core.dll")
            .Set("Category", "IDE")
            .Set("Priority", 1));

        ct.Row(r => r
            .Set("ComponentId", "webserver.exe")
            .Set("Category", "WebTools")
            .Set("Priority", 2));

        ct.Row(r => r
            .Set("ComponentId", "sqlexplorer.exe")
            .Set("Category", "DatabaseTools")
            .Set("Priority", 3));

        ct.Row(r => r
            .Set("ComponentId", "cloud.cli.exe")
            .Set("Category", "CloudTools")
            .Set("Priority", 4));

        ct.Row(r => r
            .Set("ComponentId", "diagnostics.exe")
            .Set("Category", "Diagnostics")
            .Set("Priority", 5));
    });

    // ──────────────────────────────────────────────────────────────────
    // Properties
    // ──────────────────────────────────────────────────────────────────
    pkg.Property("ARPPRODUCTICON", "apex.ico");
    pkg.Property("ALLUSERS", "1");

    // ──────────────────────────────────────────────────────────────────
    // Font registration
    // ──────────────────────────────────────────────────────────────────
    pkg.Font(Path.Combine(payloadDir, "fonts", "ApexMono.ttf"), f =>
    {
        f.Title = "Apex Mono";
    });
}, new MsiCompiler());
