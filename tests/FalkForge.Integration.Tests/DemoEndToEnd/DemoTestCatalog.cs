namespace FalkForge.Integration.Tests.DemoEndToEnd;

public static class DemoTestCatalog
{
    private static readonly string DemoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "demo"));

    private static string DemoProject(string folder, string csproj) =>
        Path.Combine(DemoRoot, folder, csproj);

    private static readonly string[] BaseMsiTables =
        ["File", "Component", "Directory", "Feature"];

    private static string[] WithExtra(params string[] extra)
    {
        var result = new string[BaseMsiTables.Length + extra.Length];
        BaseMsiTables.CopyTo(result, 0);
        extra.CopyTo(result, BaseMsiTables.Length);
        return result;
    }

    public static IReadOnlyList<DemoExpectation> AllDemos { get; } = CreateAll();

    public static IEnumerable<DemoExpectation> MsiDemos =>
        AllDemos.Where(d => d.OutputType == DemoOutputType.Msi);

    public static IEnumerable<DemoExpectation> BundleDemos =>
        AllDemos.Where(d => d.OutputType == DemoOutputType.Bundle);

    public static IEnumerable<object[]> MsiDemosData =>
        MsiDemos.Select(d => new object[] { d });

    public static IEnumerable<object[]> BundleDemosData =>
        BundleDemos.Select(d => new object[] { d });

    public static IEnumerable<object[]> AllDemosData =>
        AllDemos.Select(d => new object[] { d });

    private static List<DemoExpectation> CreateAll() =>
    [
        // === Tier 0: Original catalog entries ===

        new("01-hello-world",
            DemoProject("01-hello-world", "01-hello-world.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("02-notepad-clone",
            DemoProject("02-notepad-clone", "02-notepad-clone.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("03-client-server",
            DemoProject("03-client-server", "03-client-server.csproj"),
            DemoOutputType.Msi,
            WithExtra("ServiceInstall")),

        new("04-dev-toolkit",
            DemoProject("04-dev-toolkit", "04-dev-toolkit.csproj"),
            DemoOutputType.Msi,
            WithExtra("Environment", "Registry")),

        new("05-enterprise-suite",
            DemoProject("05-enterprise-suite", "05-enterprise-suite.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 07-extensions-showcase: Firewall/IIS/SQL/DotNet/Util extensions configure models only at build
        // time; no OS service contact during compilation. Payload files present in repo.
        new("07-extensions-showcase",
            DemoProject("07-extensions-showcase", "07-extensions-showcase.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("08-localization",
            DemoProject("08-localization", "08-localization.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("09-advanced-msi",
            DemoProject("09-advanced-msi", "09-advanced-msi.csproj"),
            DemoOutputType.Msi,
            WithExtra("CustomAction")),

        // Multi-project MSI demos (sub-projects of 06 and 10)
        new("06-product-suite/app-installer",
            DemoProject("06-product-suite", Path.Combine("app-installer", "app-installer.csproj")),
            DemoOutputType.Msi,
            BaseMsiTables),

        new("06-product-suite/service-installer",
            DemoProject("06-product-suite", Path.Combine("service-installer", "service-installer.csproj")),
            DemoOutputType.Msi,
            WithExtra("ServiceInstall")),

        new("10-advanced-bundle/msi-package",
            DemoProject("10-advanced-bundle", Path.Combine("msi-package", "msi-package.csproj")),
            DemoOutputType.Msi,
            BaseMsiTables),

        // Bundle demos
        new("06-product-suite/suite-bundle",
            DemoProject("06-product-suite", Path.Combine("suite-bundle", "suite-bundle.csproj")),
            DemoOutputType.Bundle,
            []),

        new("10-advanced-bundle/bundle",
            DemoProject("10-advanced-bundle", Path.Combine("bundle", "bundle.csproj")),
            DemoOutputType.Bundle,
            []),

        // === Tier 1: Standard MSI (base tables only or common extras) ===

        // 16-features: nested feature tree + MajorUpgrade → Upgrade table
        new("16-features",
            DemoProject("16-features", "16-features.csproj"),
            DemoOutputType.Msi,
            WithExtra("Upgrade")),

        // 18-environment-variables: system + user env vars → Environment table
        new("18-environment-variables",
            DemoProject("18-environment-variables", "18-environment-variables.csproj"),
            DemoOutputType.Msi,
            WithExtra("Environment")),

        // 21-launch-conditions: Require() calls → LaunchCondition table
        new("21-launch-conditions",
            DemoProject("21-launch-conditions", "21-launch-conditions.csproj"),
            DemoOutputType.Msi,
            WithExtra("LaunchCondition")),

        // 22-ini-files: IniFile() calls → IniFile table
        new("22-ini-files",
            DemoProject("22-ini-files", "22-ini-files.csproj"),
            DemoOutputType.Msi,
            WithExtra("IniFile")),

        // 24-fonts: Font() calls → Font table
        new("24-fonts",
            DemoProject("24-fonts", "24-fonts.csproj"),
            DemoOutputType.Msi,
            WithExtra("Font")),

        // 27-gac-assembly: GacAssembly() → MsiAssembly + MsiAssemblyName tables
        new("27-gac-assembly",
            DemoProject("27-gac-assembly", "27-gac-assembly.csproj"),
            DemoOutputType.Msi,
            WithExtra("MsiAssembly", "MsiAssemblyName")),

        // 47-powershell-actions: PowerShell custom actions → CustomAction + Binary tables
        new("47-powershell-actions",
            DemoProject("47-powershell-actions", "47-powershell-actions.csproj"),
            DemoOutputType.Msi,
            WithExtra("CustomAction", "Binary")),

        // 51-ice-validation: MajorUpgrade + Downgrade → Upgrade table
        // RequiresInfrastructure: ice.ReportPath("output/ice-report.json") requires pre-created output/ dir
        new("51-ice-validation",
            DemoProject("51-ice-validation", "51-ice-validation.csproj"),
            DemoOutputType.Msi,
            WithExtra("Upgrade"),
            RequiresInfrastructure: true),

        // === Tier 2: MSI with extra tables ===

        // 17-services: Service() + ServiceControl() → ServiceInstall + ServiceControl
        // DEMO BUG: Both ServiceControl() calls omit .Id() → empty-string PK → duplicate PK in ServiceControl table.
        // Fix: add .Id("SvcCtrl_DemoService") and .Id("SvcCtrl_DemoWorker") in the demo's Program.cs.
        // Marked RequiresInfrastructure to keep suite green until the demo is corrected.
        new("17-services",
            DemoProject("17-services", "17-services.csproj"),
            DemoOutputType.Msi,
            WithExtra("ServiceInstall", "ServiceControl"),
            RequiresInfrastructure: true),

        // 19-file-associations: FileAssociation() → Extension + Verb + MIME + ProgId
        // DEMO BUG: FileAssociation(".demo") omits .ProgId(...) → FAS002 validation error.
        // Fix: add fa.ProgId("Demo.Document") (or similar) in the demo's Program.cs.
        // Marked RequiresInfrastructure to keep suite green until the demo is corrected.
        new("19-file-associations",
            DemoProject("19-file-associations", "19-file-associations.csproj"),
            DemoOutputType.Msi,
            WithExtra("Extension", "Verb", "MIME", "ProgId"),
            RequiresInfrastructure: true),

        // 20-custom-actions: Binary() + CustomAction() calls → CustomAction + Binary
        // RequiresInfrastructure: payload/CustomActions.dll and payload/setup.ps1 not present in repo
        new("20-custom-actions",
            DemoProject("20-custom-actions", "20-custom-actions.csproj"),
            DemoOutputType.Msi,
            WithExtra("CustomAction", "Binary"),
            RequiresInfrastructure: true),

        // 23-permissions: User-driven Permission() → LockPermissions; SDDL Permission() → MsiLockPermissionsEx
        // DEMO BUG: Demo mixes User/Domain and SDDL permissions → PRM004 validation error.
        // MSI allows only LockPermissions OR MsiLockPermissionsEx per database, not both.
        // Fix: split into two separate demos, or use only one permission style.
        // Marked RequiresInfrastructure to keep suite green until the demo is corrected.
        new("23-permissions",
            DemoProject("23-permissions", "23-permissions.csproj"),
            DemoOutputType.Msi,
            WithExtra("LockPermissions", "MsiLockPermissionsEx"),
            RequiresInfrastructure: true),

        // 25-file-operations: CreateFolder + DuplicateFile + RemoveFile
        // RequiresInfrastructure: payload/debug-tools.exe not present in repo
        new("25-file-operations",
            DemoProject("25-file-operations", "25-file-operations.csproj"),
            DemoOutputType.Msi,
            WithExtra("CreateFolder", "DuplicateFile", "RemoveFile"),
            RequiresInfrastructure: true),

        // 26-custom-tables: defines custom table "AppConfig"
        new("26-custom-tables",
            DemoProject("26-custom-tables", "26-custom-tables.csproj"),
            DemoOutputType.Msi,
            WithExtra("AppConfig")),

        // 28-sequence-scheduling: SetProperty CustomAction + ExecuteSequence/UISequence entries → CustomAction
        new("28-sequence-scheduling",
            DemoProject("28-sequence-scheduling", "28-sequence-scheduling.csproj"),
            DemoOutputType.Msi,
            WithExtra("CustomAction")),

        // 32-ext-dotnet: DotNet extension is detection-only (no MSI tables); Require() → LaunchCondition
        new("32-ext-dotnet",
            DemoProject("32-ext-dotnet", "32-ext-dotnet.csproj"),
            DemoOutputType.Msi,
            WithExtra("LaunchCondition")),

        // 33-ext-util: UtilExtension configured but not wired into MSI builder → base tables only
        new("33-ext-util",
            DemoProject("33-ext-util", "33-ext-util.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 34-ext-dependency: DependencyExtension configured but not wired into MSI builder → base tables only
        new("34-ext-dependency",
            DemoProject("34-ext-dependency", "34-ext-dependency.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 48-com-registration: ComClass() → Class + ProgId tables
        new("48-com-registration",
            DemoProject("48-com-registration", "48-com-registration.csproj"),
            DemoOutputType.Msi,
            WithExtra("Class", "ProgId")),

        // === Tier 6: Infrastructure-required (IIS/SQL/Firewall/HTTP service needed at build time) ===

        // 29-ext-firewall: FirewallExtension builds MSI custom tables describing rules;
        // no OS Firewall API called at compile time. Payload present in repo.
        new("29-ext-firewall",
            DemoProject("29-ext-firewall", "29-ext-firewall.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 30-ext-iis: IisExtension builds MSI custom tables describing app pools/sites;
        // no IIS metabase contact at compile time. Payload present in repo.
        new("30-ext-iis",
            DemoProject("30-ext-iis", "30-ext-iis.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 31-ext-sql: SqlExtension builds MSI custom tables describing DB/scripts;
        // no SQL Server contact at compile time. Payload present in repo.
        new("31-ext-sql",
            DemoProject("31-ext-sql", "31-ext-sql.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // 49-http-extension: HttpExtension builds URL reservation + SNI binding tables;
        // no http.sys API called at compile time. Payload present in repo.
        new("49-http-extension",
            DemoProject("49-http-extension", "49-http-extension.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables),

        // === Tier 3: Custom UI MSI ===
        // These demos are WPF installer UI shells (InstallerApp.Run) that wrap a running Engine process.
        // dotnet run on them would launch a WPF window, not produce an MSI artifact in headless CI.
        // Marked RequiresInfrastructure until the fixture supports headless UI-shell builds.

        // 11-custom-ui-simple: InstallerApp.Run WPF shell (WelcomePage / ProgressPage / CompletePage)
        new("11-custom-ui-simple",
            DemoProject("11-custom-ui-simple", "11-custom-ui-simple.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        // 12-custom-ui-vstyle: VS-style dark-theme WPF shell (ProductPage / WorkloadsPage / ProgressPage / CompletePage)
        new("12-custom-ui-vstyle",
            DemoProject("12-custom-ui-vstyle", "12-custom-ui-vstyle.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        // 13-glass-ui: Custom borderless WPF shell with GlassWindow + InstallPage
        new("13-glass-ui",
            DemoProject("13-glass-ui", "13-glass-ui.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        // 14-lifecycle-hooks: WPF shell demonstrating all detect/plan/apply lifecycle hooks and SetSecureProperty
        new("14-lifecycle-hooks",
            DemoProject("14-lifecycle-hooks", "14-lifecycle-hooks.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        // === Tier 4: Bundles ===

        // 15-bundle-signing/msi-package: minimal MSI with MajorUpgrade + code-signing configuration.
        // RequiresInfrastructure: payload/app.exe + signtool.exe + certificate thumbprint ABC123DEF456 needed.
        new("15-bundle-signing/msi-package",
            DemoProject("15-bundle-signing", Path.Combine("msi-package", "msi-package.csproj")),
            DemoOutputType.Msi,
            WithExtra("Upgrade"),
            RequiresInfrastructure: true),

        // 15-bundle-signing/bundle: FALKBUNDLE EXE wrapping msi-package; demonstrates detach/sign/reattach workflow.
        // RequiresInfrastructure: depends on msi-package (above) which itself requires infrastructure.
        new("15-bundle-signing/bundle",
            DemoProject("15-bundle-signing", Path.Combine("bundle", "bundle.csproj")),
            DemoOutputType.Bundle,
            [],
            RequiresInfrastructure: true),

        // 35-bundle-simple: bundle with RelatedBundle + DependencyProvider wrapping MyApp.msi.
        // Payload stub provisioned at test time by PayloadProvisioner.
        new("35-bundle-simple",
            DemoProject("35-bundle-simple", "35-bundle-simple.csproj"),
            DemoOutputType.Bundle,
            []),

        // 36-bundle-exe-package: bundle with EXE prerequisite (vcredist_x64.exe) + exit code mapping.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("36-bundle-exe-package",
            DemoProject("36-bundle-exe-package", "36-bundle-exe-package.csproj"),
            DemoOutputType.Bundle,
            []),

        // 37-bundle-msu-package: bundle with Windows Update (.msu) hotfix prerequisite.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("37-bundle-msu-package",
            DemoProject("37-bundle-msu-package", "37-bundle-msu-package.csproj"),
            DemoOutputType.Bundle,
            []),

        // 38-bundle-nested: parent bundle wrapping a child bundle (.exe) and an MSI.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("38-bundle-nested",
            DemoProject("38-bundle-nested", "38-bundle-nested.csproj"),
            DemoOutputType.Bundle,
            []),

        // 39-bundle-remote-payload: bundle where the MSI is declared as a remote payload (URL + SHA256).
        // No embedded payload → BundleCompiler skips file-existence check → compiles clean without infra.
        new("39-bundle-remote-payload",
            DemoProject("39-bundle-remote-payload", "39-bundle-remote-payload.csproj"),
            DemoOutputType.Bundle,
            []),

        // 40-bundle-variables: bundle with Numeric/String/Persisted/Hidden/Secret variables + conditional package.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("40-bundle-variables",
            DemoProject("40-bundle-variables", "40-bundle-variables.csproj"),
            DemoOutputType.Bundle,
            []),

        // 41-bundle-rollback: bundle with explicit RollbackBoundary items separating prerequisites from app.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("41-bundle-rollback",
            DemoProject("41-bundle-rollback", "41-bundle-rollback.csproj"),
            DemoOutputType.Bundle,
            []),

        // 42-bundle-update-feed: bundle configured with an automatic update-feed URL.
        // Payload stub provisioned at test time by PayloadProvisioner.
        new("42-bundle-update-feed",
            DemoProject("42-bundle-update-feed", "42-bundle-update-feed.csproj"),
            DemoOutputType.Bundle,
            []),

        // 43-bundle-layout: bundle with named containers for offline layout scenarios.
        // Payload stubs provisioned at test time by PayloadProvisioner.
        new("43-bundle-layout",
            DemoProject("43-bundle-layout", "43-bundle-layout.csproj"),
            DemoOutputType.Bundle,
            []),

        // === Tier 5: MSM / MSP / MST / Driver / Delta ===

        // 44-merge-module: reusable component package compiled to .msm.
        // payload/shared.dll is present in repo — compiles headless.
        new("44-merge-module",
            DemoProject("44-merge-module", "44-merge-module.csproj"),
            DemoOutputType.MergeModule,
            []),

        new("45-patch",
            DemoProject("45-patch", "45-patch.csproj"),
            DemoOutputType.Patch,
            []),

        new("46-transform",
            DemoProject("46-transform", "46-transform.csproj"),
            DemoOutputType.Transform,
            []),

        // 50-driver-install: MSI with INF-based driver registration via FalkForge.Extensions.Driver.
        // RequiresInfrastructure: FalkForge.Extensions.Driver project does not yet exist in src/.
        new("50-driver-install",
            DemoProject("50-driver-install", "50-driver-install.csproj"),
            DemoOutputType.Msi,
            BaseMsiTables,
            RequiresInfrastructure: true),

        new("53-delta-updates",
            DemoProject("53-delta-updates", "53-delta-updates.csproj"),
            DemoOutputType.Bundle,
            []),
    ];
}
