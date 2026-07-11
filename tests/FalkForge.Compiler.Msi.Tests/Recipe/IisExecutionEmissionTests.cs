using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the IIS extension's application-pool/web-site definitions reach the COMPILED MSI as genuinely
/// scheduled deferred custom actions — pool create + rollback-remove + uninstall-remove, site create with
/// ALL bindings, and the secure credential channel (type-51 <c>SetProperty</c> carrying a property token,
/// never a plaintext password) — mirroring <see cref="SqlExecutionEmissionTests"/>. Before this branch the
/// IIsAppPool/IIsWebSite tables were inert: nothing ran them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IisExecutionEmissionTests
{
    private const int InScript = CustomActionType.InScript;
    private const int NoImpersonate = CustomActionType.NoImpersonate;
    private const int Rollback = CustomActionType.Rollback;
    private const int SetProperty = CustomActionType.SetProperty;

    [Fact]
    public void CreatePoolAndSite_ProducesDeferredCreateRollbackAndUninstallRemove()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        var poolRef = iis.DefineAppPool(p => p.Id("WebPool").Name("WebPool").Runtime("v4.0"));
        iis.AddWebSite(s => s
            .Id("WebSite").Description("FalkForgeSite").Directory("[INSTALLDIR]web")
            .Binding(80).AppPool(poolRef));

        using var db = Compile(scratch, "IisExecCreateApp", iis);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Pool create: deferred + SYSTEM, drives Microsoft.Web.Administration.
        var poolCreate = actions["IisPool_WebPool"];
        Assert.True((poolCreate.Type & InScript) != 0 && (poolCreate.Type & NoImpersonate) != 0,
            "pool create action must be deferred + SYSTEM");
        Assert.Contains("Microsoft.Web.Administration", DecodeEncoded(poolCreate.Target), StringComparison.Ordinal);

        // Pool rollback-remove scheduled before the create action.
        Assert.True((actions["IisPool_WebPool_rb"].Type & Rollback) != 0);
        Assert.Contains("ApplicationPools.Remove", DecodeEncoded(actions["IisPool_WebPool_rb"].Target), StringComparison.Ordinal);
        Assert.True(sequence["IisPool_WebPool_rb"] < sequence["IisPool_WebPool"]);

        // Site create runs AFTER the pool is created (pools first).
        var siteCreate = actions["IisSite_WebSite"];
        Assert.True((siteCreate.Type & InScript) != 0 && (siteCreate.Type & NoImpersonate) != 0);
        Assert.True(sequence["IisSite_WebSite"] > sequence["IisPool_WebPool"]);
        Assert.Contains("Sites.Add", DecodeEncoded(siteCreate.Target), StringComparison.Ordinal);

        // Site uninstall-remove exists; pool remove is a separate uninstall-only action (install gated off).
        Assert.Contains("Sites.Remove", DecodeEncoded(actions["IisSite_WebSite_un"].Target), StringComparison.Ordinal);
        var poolDelInstallRow = db.QueryRows(
            "SELECT `Action`, `Condition` FROM `InstallExecuteSequence` WHERE `Action`='IisPoolDel_WebPool'", 2);
        Assert.True(poolDelInstallRow.IsSuccess);
        Assert.Equal("0", Assert.Single(poolDelInstallRow.Value)[1]);
        Assert.Contains("ApplicationPools.Remove", DecodeEncoded(actions["IisPoolDel_WebPool_un"].Target), StringComparison.Ordinal);

        // Everything sequenced between InstallInitialize and InstallFinalize.
        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["IisPool_WebPool"], init + 1, finalize - 1);
        Assert.InRange(sequence["IisSite_WebSite"], init + 1, finalize - 1);
        Assert.InRange(sequence["IisSite_WebSite_un"], init + 1, finalize - 1);
        Assert.InRange(sequence["IisPoolDel_WebPool_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void UninstallSiteRemove_RunsBeforePoolRemove_InCompiledSequence()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        var poolRef = iis.DefineAppPool(p => p.Id("WebPool").Name("WebPool"));
        iis.AddWebSite(s => s.Id("WebSite").Description("FalkForgeSite").Directory("[INSTALLDIR]web").Binding(80).AppPool(poolRef));

        using var db = Compile(scratch, "IisExecUninstallOrderApp", iis);
        var sequence = QuerySequence(db);

        // On uninstall the site must be removed BEFORE its app pool is removed.
        Assert.True(sequence["IisSite_WebSite_un"] < sequence["IisPoolDel_WebPool_un"],
            "uninstall site remove must be sequenced before the app-pool remove");
    }

    [Fact]
    public void AllBindings_AppearInGeneratedSiteCommand()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        iis.AddWebSite(s => s
            .Id("MultiSite").Description("MultiBindingSite").Directory("[INSTALLDIR]web")
            .Binding(80)
            .Binding(8080, "http", "app.example.com")
            .Binding(8081, "http", "alt.example.com"));

        using var db = Compile(scratch, "IisExecBindingsApp", iis);
        string script = DecodeEncoded(QueryCustomActions(db)["IisSite_MultiSite"].Target);

        // ALL three bindings must appear — the historical bindings[1..] drop is fixed.
        Assert.Contains("*:80:", script, StringComparison.Ordinal);
        Assert.Contains("*:8080:app.example.com", script, StringComparison.Ordinal);
        Assert.Contains("*:8081:alt.example.com", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SpecificUserCredentials_EmitSecurePropertyChannel_WithNoPlaintextPassword()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        iis.AddAppPool(p => p
            .Id("SecurePool").Name("SecurePool")
            .IdentitySecure(AppPoolIdentityType.SpecificUser, "domain\\svc", "IISAPPPOOLPWD"));

        using var db = Compile(scratch, "IisExecSecureApp", iis);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        // Type-51 SetProperty carries the property TOKEN into the deferred action's CustomActionData.
        var setProp = actions["IisPool_SecurePool_d"];
        Assert.Equal(SetProperty, setProp.Type);
        Assert.Equal("IisPool_SecurePool", setProp.Source);
        Assert.Equal("[IISAPPPOOLPWD]", setProp.Target);
        Assert.True(sequence["IisPool_SecurePool_d"] < sequence["IisPool_SecurePool"]);

        // Nowhere in the compiled MSI is there a plaintext password — only the runtime property token, and
        // the script sets ProcessModel.Password from the $__arg channel value, never a baked literal.
        foreach (var (_, value) in AllCustomActionTargets(db))
        {
            Assert.DoesNotContain("secretpass", value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Password = 'domain", value, StringComparison.Ordinal);
        }

        Assert.Contains("$__pool.ProcessModel.Password = $__arg", DecodeEncoded(actions["IisPool_SecurePool"].Target), StringComparison.Ordinal);

        // The secret-carrying properties (source + deferred action) are listed in MsiHiddenProperties.
        var hidden = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");
        string hiddenList = Assert.Single(hidden.Value)[0] ?? "";
        Assert.Contains("IISAPPPOOLPWD", hiddenList, StringComparison.Ordinal);
        Assert.Contains("IisPool_SecurePool", hiddenList, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSpecificUserPool_EmitsNoMsiHiddenPropertiesRow()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        iis.AddAppPool(p => p.Id("PlainPool").Name("PlainPool").Identity(AppPoolIdentityType.ApplicationPoolIdentity));

        using var db = Compile(scratch, "IisExecPlainApp", iis);

        var hidden = db.QueryRows("SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");
        Assert.Empty(hidden.Value); // no SpecificUser password → no secret → no row
    }

    [Fact]
    public void LiteralPassword_IsEmbedded_ProvingTheSecurePathDiffers()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        // Literal password path (IIS012-warned) — embeds the value; the contrast with the secure test proves
        // the secure path genuinely keeps the secret out of the MSI.
        iis.AddAppPool(p => p
            .Id("LiteralPool").Name("LiteralPool")
            .Identity(AppPoolIdentityType.SpecificUser, "domain\\svc", "PlAiNtExT123"));

        using var db = Compile(scratch, "IisExecLiteralApp", iis);
        var setProp = QueryCustomActions(db)["IisPool_LiteralPool_d"];

        Assert.Equal("PlAiNtExT123", setProp.Target);
    }

    [Fact]
    public void GeneratedScript_FailsLoud_WhenIisNotInstalled()
    {
        using var scratch = new Scratch();

        var iis = new IisExtension();
        iis.DefineAppPool(p => p.Id("WebPool").Name("WebPool"));

        using var db = Compile(scratch, "IisExecPrereqApp", iis);
        string script = DecodeEncoded(QueryCustomActions(db)["IisPool_WebPool"].Target);

        // The deferred action checks for IIS and throws a clear error rather than silently no-opping.
        Assert.Contains("W3SVC", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves the IIS extension genuinely creates an app pool + web site on install and removes them on
    /// uninstall — not merely authored into the MSI. Gated behind <c>FALKFORGE_E2E=1</c> AND administrator
    /// elevation AND IIS present (W3SVC). Honestly skips (never a silent fake pass) when any gate is closed.
    /// </summary>
    [Fact]
    public void PoolAndSite_AreCreated_ThenRemoved_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real IIS install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (!IsElevated())
            Assert.Skip("Real IIS install requires administrator elevation; run the test host elevated.");
        if (!IisInstalled())
            Assert.Skip("IIS (W3SVC) is not installed; install the Web Server (IIS) role to run this e2e.");

        string poolName = "FalkForgeE2ePool_" + Guid.NewGuid().ToString("N")[..8];
        string siteName = "FalkForgeE2eSite_" + Guid.NewGuid().ToString("N")[..8];
        int port = 51000 + (Environment.TickCount & 0x0FFF);
        using var scratch = new Scratch();

        var iis = new IisExtension();
        var poolRef = iis.DefineAppPool(p => p.Id(poolName).Name(poolName).Runtime("v4.0"));
        iis.AddWebSite(s => s.Id(siteName).Description(siteName).Directory(scratch.SourceDir).Binding(port).AppPool(poolRef));

        var package = MinimalPackage(scratch, "IisExecE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(iis).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(AppcmdListSucceeds($"list apppool \"{poolName}\""), "app pool was not created by the deferred action");
            Assert.True(AppcmdListSucceeds($"list site \"{siteName}\""), "web site was not created by the deferred action");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(AppcmdListSucceeds($"list site \"{siteName}\""), "web site was not removed on uninstall");
            Assert.False(AppcmdListSucceeds($"list apppool \"{poolName}\""), "app pool was not removed on uninstall");
        }
        finally
        {
            RunAppcmd($"delete site \"{siteName}\"");
            RunAppcmd($"delete apppool \"{poolName}\"");
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static MsiDatabase Compile(Scratch scratch, string name, IisExtension iis)
    {
        var package = MinimalPackage(scratch, name);
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(iis).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for iis execution emission test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
    }

    private static Dictionary<string, (int Type, string Source, string Target)> QueryCustomActions(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4);
        Assert.True(rows.IsSuccess, $"CustomAction query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, (int, string, string)>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
        {
            int type = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);
            map[r[0] ?? ""] = (type, r[2] ?? "", r[3] ?? "");
        }

        return map;
    }

    private static IEnumerable<(string Action, string Target)> AllCustomActionTargets(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Target` FROM `CustomAction`", 2);
        Assert.True(rows.IsSuccess);
        foreach (var r in rows.Value)
            yield return (r[0] ?? "", r[1] ?? "");
    }

    private static Dictionary<string, int> QuerySequence(MsiDatabase db)
    {
        var rows = db.QueryRows("SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`", 2);
        Assert.True(rows.IsSuccess, $"InstallExecuteSequence query failed: {(rows.IsFailure ? rows.Error.Message : "")}");

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Value)
            map[r[0] ?? ""] = int.Parse(r[1] ?? "0", CultureInfo.InvariantCulture);

        return map;
    }

    private static string DecodeEncoded(string target)
    {
        const string marker = "-EncodedCommand ";
        int idx = target.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Target is not an -EncodedCommand invocation: {target}");
        int end = target.IndexOf(" \"", idx, StringComparison.Ordinal);
        string base64 = (end >= 0 ? target[(idx + marker.Length)..end] : target[(idx + marker.Length)..]).Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IisInstalled()
        => File.Exists(AppcmdPath()) && RunAppcmd("list sites") == 0;

    private static int RunMsiExec(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments) { UseShellExecute = false })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string AppcmdPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "inetsrv", "appcmd.exe");

    private static bool AppcmdListSucceeds(string arguments) => RunAppcmd(arguments) == 0;

    private static int RunAppcmd(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(AppcmdPath(), arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            process.WaitForExit();
            // appcmd "list" returns 0 when the object exists, non-zero when it does not.
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -1;
        }
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"IisExecEmit_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
