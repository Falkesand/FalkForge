using System.Text.Json;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Builders;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Models;
using FalkForge.Platform.Windows;

// ═════════════════════════════════════════════════════════════════════════════════════════════
//  Demo 64 — AcmeSuite Enterprise (the capstone "put it all together" walkthrough)
// ═════════════════════════════════════════════════════════════════════════════════════════════
//
// Every other demo isolates ONE capability. This one composes the whole enterprise stack the way a
// real product ships it, so you can see the pieces working together rather than in isolation:
//
//   • A three-tier FEATURE TREE — Server / Client / Tools — the user can select at install time.
//   • A WINDOWS SERVICE gated to the Server feature (declared through FeatureBuilder.Service, so
//     the compiler stamps its component under the Server feature — install Server, get the service;
//     skip Server, and neither the files nor the service are laid down).
//   • REGISTRY configuration: shared product keys at package scope, plus per-feature keys gated to
//     the Server and Tools features.
//   • An IIS application pool + web site + binding, provisioned by the IIS extension. The app-pool
//     runs as a specific user whose password is supplied SECURELY at run time via an MSI property
//     (IdentitySecure/PasswordProperty) — never stored in the MSI.
//   • A SQL Server database + a schema script, provisioned by the SQL extension, authenticating
//     with a SQL login whose password is likewise supplied SECURELY via an MSI property.
//   • A CUSTOM ACTION scheduled the correct way — through ExecuteSequence(...), which is the ONLY
//     scheduling channel the compiler reads (the CustomActionBuilder.After/Before fields are inert).
//   • Start-menu SHORTCUTS and a FILE ASSOCIATION under the Client feature.
//   • Finally the MSI is wrapped, together with a prerequisite runtime MSI, into a self-extracting
//     EXE BUNDLE that is code-SIGNED (ECDSA payload integrity) and wired to an auto-UPDATE FEED.
//
// The multi-secret angle is deliberate. Using BOTH the IIS secure password AND the SQL secure
// password in one package exercises the aggregation of every secret property name into a single
// `MsiHiddenProperties` row. Before that fix each secret-bearing extension authored its own row
// keyed on the same primary key and the build failed on a duplicate PK — so the fact that this
// demo BUILDS at all is itself the proof the multi-secret story now works end to end.
//
// One `dotnet run` produces two artifacts in the output directory:
//   • AcmeSuite Enterprise.msi        — the composed product installer
//   • AcmeSuite Enterprise Setup.exe  — the signed, auto-updating bundle wrapping it
//
// Honest scope note: this program only *builds* the packages. Actually creating the IIS site,
// the SQL database and the Windows service happens when the MSI is installed on a machine with
// administrator rights, IIS and SQL Server present. The IIS certificate binding (HTTPS) is left
// as configuration only — certificate provisioning is a documented follow-up in the IIS extension.
// ═════════════════════════════════════════════════════════════════════════════════════════════

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

// The whole demo is driven through Installer.BuildBundle: it resolves the output directory from
// -o/--output (default: current directory), and we build the MSI(s) first, then the bundle.
return Installer.BuildBundle(args, outputDir =>
{
    // 1. Build the composed AcmeSuite MSI straight into the output directory (a durable artifact).
    var mainMsi = BuildAcmeSuiteMsi(payloadDir, outputDir);
    if (mainMsi.IsFailure)
        return mainMsi;
    Console.WriteLine($"AcmeSuite MSI created: {mainMsi.Value}");

    // 2. Build the small prerequisite runtime MSI into a temp directory (it is only ever consumed
    //    by the bundle, so it does not need to survive as a standalone artifact).
    var tempDir = Directory.CreateTempSubdirectory("acmesuite-").FullName;
    try
    {
        var prereqMsi = BuildPrerequisiteMsi(payloadDir, tempDir);
        if (prereqMsi.IsFailure)
            return prereqMsi;

        // 3. Wrap [prerequisite, main] into a signed, auto-updating EXE bundle.
        var bundle = BuildBundle(prereqMsi.Value, mainMsi.Value, outputDir);
        if (bundle.IsSuccess)
            PrintSignatureConfirmation(bundle.Value);
        return bundle;
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
});

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  The composed product MSI
// ─────────────────────────────────────────────────────────────────────────────────────────────
static Result<string> BuildAcmeSuiteMsi(string payloadDir, string outputDir)
{
    // Install layout: everything lives under Program Files\Acme Corporation\AcmeSuite\...
    var installDir = KnownFolder.ProgramFiles / "Acme Corporation" / "AcmeSuite";
    var serverDir = installDir / "Server";
    var wwwrootDir = serverDir / "wwwroot";
    var clientDir = installDir / "Client";
    var toolsDir = installDir / "Tools";

    // The installed path to the service executable, written the MSI way with a directory property.
    const string serverExeInstalled = @"[ProgramFilesFolder]Acme Corporation\AcmeSuite\Server\AcmeServer.exe";

    // ── The two extensions that provision infrastructure. Both use the SECURE password path:
    //    the password is never embedded in the MSI; at install time a custom-UI installer (or the
    //    command line) supplies it via SetSecureProperty, and the extension routes it to a deferred,
    //    elevated custom action through the redacted CustomActionData channel.

    // IIS: an app pool running as a specific user (password from the IISAPPPOOLPWD property), a web
    // site rooted at the installed wwwroot directory, and a plain HTTP binding on port 80.
    // Identifiers are deliberately short: the IIS secure-pool create step is a large PowerShell
    // command, and MSI caps a CustomAction target at 4096 characters — long ids/paths can push it
    // over. The web root is expressed relative to the install folder to keep the command compact.
    var iis = new IisExtension();
    var appPool = iis.DefineAppPool(pool => pool
        .Id("AcmePool")
        .Name("AcmePool")
        .PipelineMode(ManagedPipelineMode.Integrated)
        .IdentitySecure(AppPoolIdentityType.SpecificUser, "ACME\\svcweb", "IISAPPPOOLPWD"));

    iis.AddWebSite(site => site
        .Id("AcmeSite")
        .Description("AcmeSuite")
        .Directory("[INSTALLFOLDER]Server\\wwwroot")
        .AppPool(appPool)
        .Binding(80)
        .AutoStart(true));
    // HTTPS/certificate binding is configuration-only today (see IIS extension follow-up); omitted
    // here rather than declared as if it fully provisioned a certificate at runtime.

    // SQL: create the AcmeSuite database and run the schema script, authenticating as a SQL login
    // whose password comes from the SQLPASSWORD property (secure path).
    var sql = new SqlExtension();
    var dbRef = sql.DefineDatabase(db => db
        .Id("AcmeDb")
        .Server(".")
        .Database("AcmeSuite")
        .CreateOnInstall()
        .ConfirmOverwrite()
        .User("acme_app")
        .PasswordProperty("SQLPASSWORD"));
    if (dbRef.IsFailure)
        return Result<string>.Failure(dbRef.Error);

    var schemaScript = new SqlScriptBuilder()
        .Id("AcmeSchema")
        .Database(dbRef.Value)
        .SourceFile(Path.Combine(payloadDir, "sql", "schema.sql"))
        .ExecuteOnInstall()
        .Sequence(1)
        .Build();
    if (schemaScript.IsFailure)
        return Result<string>.Failure(schemaScript.Error);
    sql.Scripts.Add(schemaScript.Value);

    var builder = new PackageBuilder
    {
        Name = "AcmeSuite Enterprise",
        Manufacturer = "Acme Corporation",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("7F3C2A10-9E4B-4D6F-A1C2-000000000064"),
        Description = "AcmeSuite Enterprise — server, client and administration tooling.",
        Scope = InstallScope.PerMachine,
        Architecture = ProcessorArchitecture.X64,
        DefaultInstallDirectory = installDir
    };

    // A feature-tree dialog so the user can pick which tiers to install.
    builder.UseDialogSet(MsiDialogSet.FeatureTree);

    // ── SERVER feature: server binary + web content, the Windows service, and server registry keys.
    builder.Feature("Server", server =>
    {
        server.Title = "Server";
        server.Description = "AcmeSuite server host, IIS site and background service.";
        server.IsDefault = true;

        server.Files(f => f
            .Add(Path.Combine(payloadDir, "AcmeServer.exe"))
            .To(serverDir));
        server.Files(f => f
            .Add(Path.Combine(payloadDir, "wwwroot", "index.html"))
            .To(wwwrootDir));

        // The service is stamped with the Server feature id: install Server, get the service.
        server.Service("AcmeServer", svc =>
        {
            svc.DisplayName = "AcmeSuite Server";
            svc.Description = "Hosts the AcmeSuite background server.";
            svc.Executable = serverExeInstalled;
            svc.StartMode = ServiceStartMode.Automatic;
            svc.Account = ServiceAccount.LocalSystem;
        });

        // Server-only configuration keys — gated to the Server feature.
        server.Registry(r => r
            .Key(RegistryRoot.LocalMachine, @"Software\Acme Corporation\AcmeSuite\Server", k => k
                .Value("ListenPort", "80")
                .Value("SiteName", "AcmeSite")
                .DWord("Enabled", 1)));
    });

    // ── CLIENT feature: desktop client, a Start-menu shortcut, and a file association.
    builder.Feature("Client", client =>
    {
        client.Title = "Client";
        client.Description = "AcmeSuite desktop client with shortcut and file association.";
        client.IsDefault = true;

        client.Files(f => f
            .Add(Path.Combine(payloadDir, "AcmeClient.exe"))
            .To(clientDir));

        // Start-menu shortcut under an "AcmeSuite" program group.
        client.Shortcut("AcmeSuite Client", "AcmeClient.exe")
            .WithDescription("Launch the AcmeSuite desktop client")
            .OnStartMenu("AcmeSuite");

        // Associate the ".acme" document type with the client.
        client.FileAssociation(".acme", fa =>
        {
            fa.ProgId("AcmeSuite.Document");
            fa.Description = "AcmeSuite Document";
            fa.ContentType = "application/x-acmesuite";
            fa.IconFile = Path.Combine(payloadDir, "AcmeClient.exe");
            fa.IconIndex = 0;
            fa.Verb(ShellVerb.Open, "\"%1\"", verb => verb.Command = "Open");
        });
    });

    // ── TOOLS feature: optional administration utilities (off by default), with their own reg key.
    builder.Feature("Tools", tools =>
    {
        tools.Title = "Administration Tools";
        tools.Description = "Optional command-line administration utilities.";
        tools.IsDefault = false;

        tools.Files(f => f
            .Add(Path.Combine(payloadDir, "AcmeAdmin.exe"))
            .To(toolsDir));

        tools.Registry(r => r
            .Key(RegistryRoot.LocalMachine, @"Software\Acme Corporation\AcmeSuite\Tools", k => k
                .Value("Installed", "1")));
    });

    // ── Shared, non-feature-gated product registry (always installed).
    builder.Registry(r => r
        .Key(RegistryRoot.LocalMachine, @"Software\Acme Corporation\AcmeSuite", k => k
            .Value("Version", "1.0.0")
            .Value("InstallPath", "[INSTALLFOLDER]")));

    // ── Custom action, scheduled the ONLY correct way — through ExecuteSequence.
    //    (Setting ca.After/ca.Before on the builder would be ignored by the compiler.)
    builder.CustomAction("SetDeploymentTier", ca =>
    {
        ca.SetProperty("ACMEDEPLOYTIER", "Enterprise");
        ca.Condition = "NOT Installed";
    });
    builder.ExecuteSequence(seq => seq
        .Action("SetDeploymentTier")
            .After("CostFinalize")
            .Condition("NOT Installed"));

    // Secure properties the IIS and SQL secure paths read at run time. Declared public (uppercase)
    // and empty here; a custom-UI installer supplies the real values via SetSecureProperty. The
    // extensions add these names to MsiHiddenProperties so they are redacted from verbose MSI logs.
    builder.Property("IISAPPPOOLPWD", "", prop => prop.IsSecure = true);
    builder.Property("SQLPASSWORD", "", prop => prop.IsSecure = true);

    // Standard upgrade behaviour.
    builder.MajorUpgrade(mu => mu.MigrateFeatures(true));
    builder.Downgrade(d => d.Block("A newer version of AcmeSuite is already installed."));

    var package = builder.Build();

    // Attach both extensions to the compiler with a single .Use(...) call. This is where the
    // multi-secret aggregation happens: SQL and IIS each declare their secret property names, and
    // the compiler merges them into ONE MsiHiddenProperties row (no duplicate-PK collision).
    return new MsiCompiler(new WindowsFileSystem())
        .Use(iis, sql)
        .Compile(package, outputDir);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  The prerequisite runtime MSI (a stand-in for e.g. a shared runtime redistributable)
// ─────────────────────────────────────────────────────────────────────────────────────────────
static Result<string> BuildPrerequisiteMsi(string payloadDir, string tempDir)
{
    var installDir = KnownFolder.ProgramFiles / "Acme Corporation" / "Acme Runtime";

    var builder = new PackageBuilder
    {
        Name = "Acme Runtime",
        Manufacturer = "Acme Corporation",
        Version = new Version(1, 0, 0),
        UpgradeCode = new Guid("7F3C2A10-9E4B-4D6F-A1C2-0000000000AC"),
        Description = "Shared runtime prerequisite for AcmeSuite.",
        DefaultInstallDirectory = installDir
    };
    builder.UseDialogSet(MsiDialogSet.Minimal);
    builder.Files(f => f.Add(Path.Combine(payloadDir, "AcmeRuntime.dll")).To(installDir));
    builder.MajorUpgrade(_ => { });
    builder.Downgrade(d => d.Block("A newer version of the Acme Runtime is already installed."));

    var package = builder.Build();
    var msiDir = Path.Combine(tempDir, "prereq");
    Directory.CreateDirectory(msiDir);
    return new MsiCompiler(new WindowsFileSystem()).Compile(package, msiDir);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
//  The signed, auto-updating bundle wrapping [prerequisite, main]
// ─────────────────────────────────────────────────────────────────────────────────────────────
static Result<string> BuildBundle(string prereqMsiPath, string mainMsiPath, string outputDir)
{
    var bundle = new BundleBuilder()
        .Name("AcmeSuite Enterprise Setup")
        .Manufacturer("Acme Corporation")
        .Version("1.0.0")
        .BundleId(new Guid("7F3C2A10-9E4B-4D6F-A1C2-0000000000B0"))
        .UpgradeCode(new Guid("7F3C2A10-9E4B-4D6F-A1C2-0000000000B1"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        // ECDSA payload integrity signing. `Integrity(i => { })` uses an ephemeral P-256 key
        // generated for this build; for a real release pass a stable key with i.SigningKey("key.pem")
        // so the embedded public key (the authorship proof) is identical across builds — see demo 59.
        .Integrity(i => { })
        // Auto-update feed: at startup the engine checks this feed and can download+prompt for a
        // newer signed bundle. PinUpdatePublisher would pin the update's Authenticode thumbprint;
        // omitted here because this demo signs with an ephemeral (throwaway) key.
        .UpdateFeed("https://updates.acme.example/acmesuite/feed.json", UpdatePolicy.DownloadAndPrompt)
        .Chain(chain => chain
            // Prerequisite first — installed before the main product.
            .MsiPackage(prereqMsiPath, p => p
                .Id("AcmeRuntime")
                .DisplayName("Acme Runtime 1.0")
                .Version("1.0.0")
                .Vital(true))
            // The composed AcmeSuite product.
            .MsiPackage(mainMsiPath, p => p
                .Id("AcmeSuite")
                .DisplayName("AcmeSuite Enterprise")
                .Version("1.0.0")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputDir);
}

// Reads the compiled bundle's manifest back and confirms the ECDSA integrity signature is present
// and verifies — exactly the check the engine performs before extracting any payload.
static void PrintSignatureConfirmation(string bundlePath)
{
    var content = PayloadEmbedder.Extract(bundlePath);
    if (content.IsFailure || content.Value.ManifestJsonBytes is null)
    {
        Console.WriteLine("  (could not read back bundle manifest for signature confirmation)");
        return;
    }

    var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes);
    var envelope = manifest?.ManifestSignature is null
        ? null
        : IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature);
    var verifies = envelope is not null && IntegrityEnvelopeCodec.VerifySignature(envelope);

    Console.WriteLine($"  bundle signature present: {envelope is not null}, verifies: {verifies}");
    Console.WriteLine($"  update feed: {manifest?.UpdateFeed?.FeedUrl ?? "(none)"}");
}
