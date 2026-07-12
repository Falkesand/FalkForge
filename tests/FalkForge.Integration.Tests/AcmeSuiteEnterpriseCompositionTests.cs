using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge.Builders;
using FalkForge.Compiler.Bundle;
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
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// Always-on structural guard for the capstone enterprise composition shipped as
/// <c>demo/64-acmesuite-enterprise</c>. It independently re-composes the same enterprise stack
/// through the real public APIs and proves — by compiling actual artifacts and reading them back —
/// that the whole story holds together:
/// <list type="bullet">
///   <item>a three-tier Server/Client/Tools feature tree,</item>
///   <item>a Windows service gated to the Server feature (its component maps to <c>Server</c>),</item>
///   <item>the IIS and SQL extensions' deferred, sequenced execution custom actions,</item>
///   <item>a single aggregated <c>MsiHiddenProperties</c> row listing BOTH the SQL and the IIS
///     secret property names — the multi-secret proof that would have failed with a duplicate
///     primary key before the aggregation fix, and</item>
///   <item>the wrapping bundle carrying a verified ECDSA integrity signature and the update feed.</item>
/// </list>
/// A genuine end-to-end install (creating the IIS site, SQL database and service) needs an elevated
/// host with IIS and SQL Server present; that is honestly skipped below rather than faked. The
/// compile-and-inspect proof here is the always-on guard.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AcmeSuiteEnterpriseCompositionTests
{
    [Fact]
    public void AcmeSuite_ComposesFeatureTreeServiceSecretsAndSignedBundle()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "MSI compilation is Windows-only (msi.dll).");

        using var scratch = new Scratch();
        var msiPath = CompileAcmeSuiteMsi(scratch);

        using var db = OpenDatabase(msiPath);

        // ── Feature tree: Server / Client / Tools all present.
        var features = QuerySingleColumn(db, "SELECT `Feature` FROM `Feature`");
        Assert.Contains("Server", features);
        Assert.Contains("Client", features);
        Assert.Contains("Tools", features);

        // ── The Windows service exists and its component is placed under the Server feature.
        var serviceComponent = QuerySingleColumn(db,
            "SELECT `Component_` FROM `ServiceInstall` WHERE `Name`='AcmeServer'");
        var serviceComponentId = Assert.Single(serviceComponent);
        var serviceFeature = QuerySingleColumn(db,
            $"SELECT `Feature_` FROM `FeatureComponents` WHERE `Component_`='{serviceComponentId}'");
        Assert.Equal("Server", Assert.Single(serviceFeature));

        // ── The custom action scheduled via ExecuteSequence is present AND actually sequenced.
        var customActions = QuerySingleColumn(db, "SELECT `Action` FROM `CustomAction`");
        Assert.Contains("SetDeploymentTier", customActions);
        var sequencedActions = QuerySingleColumn(db, "SELECT `Action` FROM `InstallExecuteSequence`");
        Assert.Contains("SetDeploymentTier", sequencedActions);

        // ── The IIS and SQL extensions scheduled deferred (in-script) execution custom actions.
        //    The deferred bit is msidbCustomActionTypeInScript (0x400 = 1024).
        var caTypes = QuerySingleColumn(db, "SELECT `Type` FROM `CustomAction`");
        Assert.Contains(caTypes, t => int.TryParse(t, out var type) && (type & 0x400) != 0);

        // ── The multi-secret proof: exactly ONE MsiHiddenProperties row listing BOTH the SQL and
        //    IIS secret property names (and their deferred-action CustomActionData carriers). Before
        //    the aggregation fix, SQL and IIS each authored a row keyed "MsiHiddenProperties" and the
        //    build failed on a duplicate primary key — so reaching this assertion at all is the proof.
        var hidden = QuerySingleColumn(db,
            "SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'");
        var hiddenValue = Assert.Single(hidden);
        var secretNames = hiddenValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("SQLPASSWORD", secretNames);        // SQL secure source property
        Assert.Contains("SqlDb_AcmeDb", secretNames);       // SQL deferred action's CustomActionData carrier
        Assert.Contains("IISAPPPOOLPWD", secretNames);      // IIS secure source property
        Assert.Contains("IisPool_AcmePool", secretNames);   // IIS deferred action's CustomActionData carrier
        var sorted = secretNames.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(sorted, secretNames);                  // deterministically ordered

        // ── The wrapping bundle: signed (verified ECDSA integrity) and carrying the update feed.
        var bundlePath = CompileSignedBundle(scratch, msiPath);
        var extract = PayloadEmbedder.Extract(bundlePath);
        Assert.True(extract.IsSuccess, extract.IsFailure ? extract.Error.Message : "");
        Assert.NotNull(extract.Value.ManifestJsonBytes);

        var manifest = JsonSerializer.Deserialize<InstallerManifest>(extract.Value.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        Assert.NotNull(manifest!.ManifestSignature);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!), "Bundle integrity signature must verify.");

        Assert.NotNull(manifest.UpdateFeed);
        Assert.Equal("https://updates.acme.example/acmesuite/feed.json", manifest.UpdateFeed!.FeedUrl);
    }

    [Fact]
    public void AcmeSuite_RealInstall_CreatesIisSiteSqlDbAndService()
    {
        // A genuine install must run elevated on a host with IIS and SQL Server present and would
        // mutate real machine state (create an app pool, a web site, a database and a service). None
        // of that is available or safe in CI, so this is an honest skip — never a fake pass. The
        // always-on compile+structure test above is the real guard.
        Assert.Skip("Real AcmeSuite install requires an elevated host with IIS + SQL Server; " +
                    "not run in CI. The compile/structure test is the always-on guard.");
    }

    // Re-composes the same enterprise MSI the demo builds, through the real public APIs.
    private static string CompileAcmeSuiteMsi(Scratch scratch)
    {
        var installDir = KnownFolder.ProgramFiles / "Acme Corporation" / "AcmeSuite";

        var iis = new IisExtension();
        var appPool = iis.DefineAppPool(pool => pool
            .Id("AcmePool").Name("AcmePool")
            .PipelineMode(ManagedPipelineMode.Integrated)
            .IdentitySecure(AppPoolIdentityType.SpecificUser, "ACME\\svcweb", "IISAPPPOOLPWD"));
        iis.AddWebSite(site => site
            .Id("AcmeSite").Description("AcmeSuite")
            .Directory("[INSTALLFOLDER]Server\\wwwroot")
            .AppPool(appPool).Binding(80).AutoStart(true));

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AcmeDb").Server(".").Database("AcmeSuite").CreateOnInstall().ConfirmOverwrite()
            .User("acme_app").PasswordProperty("SQLPASSWORD"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        var serverExe = Path.Combine(scratch.SourceDir, "AcmeServer.exe");
        var clientExe = Path.Combine(scratch.SourceDir, "AcmeClient.exe");
        var adminExe = Path.Combine(scratch.SourceDir, "AcmeAdmin.exe");
        File.WriteAllText(serverExe, "server");
        File.WriteAllText(clientExe, "client");
        File.WriteAllText(adminExe, "admin");

        var builder = new PackageBuilder
        {
            Name = "AcmeSuite Enterprise",
            Manufacturer = "Acme Corporation",
            Version = new Version(1, 0, 0),
            UpgradeCode = new Guid("7F3C2A10-9E4B-4D6F-A1C2-000000000064"),
            Scope = InstallScope.PerMachine,
            Architecture = ProcessorArchitecture.X64,
            DefaultInstallDirectory = installDir
        };
        builder.UseDialogSet(MsiDialogSet.FeatureTree);

        builder.Feature("Server", server =>
        {
            server.Title = "Server";
            server.Files(f => f.Add(serverExe).To(installDir / "Server"));
            server.Service("AcmeServer", svc =>
            {
                svc.DisplayName = "AcmeSuite Server";
                svc.Executable = @"[ProgramFilesFolder]Acme Corporation\AcmeSuite\Server\AcmeServer.exe";
                svc.StartMode = ServiceStartMode.Automatic;
                svc.Account = ServiceAccount.LocalSystem;
            });
            server.Registry(r => r.Key(RegistryRoot.LocalMachine,
                @"Software\Acme Corporation\AcmeSuite\Server", k => k.Value("ListenPort", "80")));
        });

        builder.Feature("Client", client =>
        {
            client.Title = "Client";
            client.IsDefault = true;
            client.Files(f => f.Add(clientExe).To(installDir / "Client"));
            client.Shortcut("AcmeSuite Client", "AcmeClient.exe")
                .WithDescription("Launch the AcmeSuite client").OnStartMenu("AcmeSuite");
            client.FileAssociation(".acme", fa =>
            {
                fa.ProgId("AcmeSuite.Document");
                fa.Description = "AcmeSuite Document";
                fa.IconFile = clientExe;
                fa.IconIndex = 0;
                fa.Verb(ShellVerb.Open, "\"%1\"", verb => verb.Command = "Open");
            });
        });

        builder.Feature("Tools", tools =>
        {
            tools.Title = "Administration Tools";
            tools.IsDefault = false;
            tools.Files(f => f.Add(adminExe).To(installDir / "Tools"));
        });

        builder.Registry(r => r.Key(RegistryRoot.LocalMachine,
            @"Software\Acme Corporation\AcmeSuite", k => k.Value("Version", "1.0.0")));

        builder.CustomAction("SetDeploymentTier", ca =>
        {
            ca.SetProperty("ACMEDEPLOYTIER", "Enterprise");
            ca.Condition = "NOT Installed";
        });
        builder.ExecuteSequence(seq => seq
            .Action("SetDeploymentTier").After("CostFinalize").Condition("NOT Installed"));

        builder.Property("IISAPPPOOLPWD", "", prop => prop.IsSecure = true);
        builder.Property("SQLPASSWORD", "", prop => prop.IsSecure = true);
        builder.MajorUpgrade(mu => mu.MigrateFeatures(true));
        builder.Downgrade(d => d.Block("A newer version of AcmeSuite is already installed."));

        var package = builder.Build();
        var result = new MsiCompiler(new WindowsFileSystem()).Use(iis, sql).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    private static string CompileSignedBundle(Scratch scratch, string mainMsiPath)
    {
        // A stub prerequisite payload — the bundle wraps [prerequisite, main].
        var prereqPath = Path.Combine(scratch.SourceDir, "AcmeRuntime.msi");
        File.WriteAllText(prereqPath, "prereq");

        var bundle = new BundleBuilder()
            .Name("AcmeSuite Enterprise Setup")
            .Manufacturer("Acme Corporation")
            .Version("1.0.0")
            .BundleId(new Guid("7F3C2A10-9E4B-4D6F-A1C2-0000000000B0"))
            .UpgradeCode(new Guid("7F3C2A10-9E4B-4D6F-A1C2-0000000000B1"))
            .Scope(InstallScope.PerMachine)
            .UseSilentUI()
            .Integrity(i => { })
            .UpdateFeed("https://updates.acme.example/acmesuite/feed.json", UpdatePolicy.DownloadAndPrompt)
            .Chain(chain => chain
                .MsiPackage(prereqPath, p => p.Id("AcmeRuntime").DisplayName("Acme Runtime 1.0").Vital(true))
                .MsiPackage(mainMsiPath, p => p.Id("AcmeSuite").DisplayName("AcmeSuite Enterprise").Vital(true)))
            .Build();

        // AllowPlaceholderStub keeps the test independent of a built NativeAOT engine stub while still
        // exercising the full manifest generation + integrity signing pipeline.
        var result = new BundleCompiler { AllowPlaceholderStub = true }.Compile(bundle, scratch.BundleDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        return result.Value;
    }

    private static MsiDatabase OpenDatabase(string msiPath)
    {
        var open = MsiDatabase.Open(msiPath, readOnly: true);
        Assert.True(open.IsSuccess, open.IsFailure ? open.Error.Message : "");
        return open.Value;
    }

    private static List<string> QuerySingleColumn(MsiDatabase db, string sql)
    {
        var rows = db.QueryRows(sql, 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        return rows.Value.Select(r => r[0] ?? "").ToList();
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"AcmeSuiteComposition_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            BundleDir = Path.Combine(_root, "bundle");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
            Directory.CreateDirectory(BundleDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }
        public string BundleDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
