using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using FalkForge.Extensions.Http;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the Http extension's URL ACL reservations and SNI SSL certificate bindings reach the
/// COMPILED MSI as genuinely-scheduled deferred, elevated (SYSTEM) custom actions — not merely authored
/// table data that nothing runs — mirroring <see cref="UtilExecutionEmissionTests"/> and
/// <see cref="UtilUserGroupExecutionEmissionTests"/>. Before this branch, <see cref="HttpExtension"/> hand-
/// authored its own <c>CustomAction</c>/<c>InstallExecuteSequence</c> rows via a bespoke, unescaped
/// contributor pair (raw string interpolation into <c>CustomAction.Target</c> — a command-injection
/// surface in a SYSTEM-context deferred action); this suite proves that path is GONE and the extension now
/// runs entirely through the shared execution seam (<c>ExecutionStepEmitter</c>), the same one Firewall and
/// Util use.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HttpExecutionEmissionTests
{
    private const int InScript = CustomActionType.InScript;
    private const int NoImpersonate = CustomActionType.NoImpersonate;
    private const int Rollback = CustomActionType.Rollback;

    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void UrlAcl_ProducesDeferredSystemInstallAction_WithRollbackAndUninstall_SequencedInExecuteScript()
    {
        using var scratch = new Scratch();

        var http = new HttpExtension();
        http.AddUrlReservation("http://+:8080/svc/", url => url.AllowNetworkService());

        using var db = Compile(scratch, "HttpUrlAclApp", http);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["HttpUrl_0"];
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0,
            "urlacl install action must be deferred + SYSTEM");
        string installScript = DecodeEncoded(install.Target);
        Assert.Contains("'http' 'add' 'urlacl'", installScript, StringComparison.Ordinal);
        Assert.Contains("'url=http://+:8080/svc/'", installScript, StringComparison.Ordinal);
        Assert.Contains("'user=D:(A;;GX;;;NS)'", installScript, StringComparison.Ordinal);

        var rollback = actions["HttpUrl_0_rb"];
        Assert.True((rollback.Type & Rollback) != 0, "urlacl install must have a rollback action");
        Assert.Contains("'http' 'delete' 'urlacl'", DecodeEncoded(rollback.Target), StringComparison.Ordinal);

        var uninstall = actions["HttpUrl_0_un"];
        Assert.True((uninstall.Type & InScript) != 0 && (uninstall.Type & NoImpersonate) != 0);
        Assert.Contains("'http' 'delete' 'urlacl'", DecodeEncoded(uninstall.Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["HttpUrl_0"], init + 1, finalize - 1);
        Assert.True(sequence["HttpUrl_0_rb"] < sequence["HttpUrl_0"], "rollback scheduled before the add");
        Assert.InRange(sequence["HttpUrl_0_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void SslCert_ProducesDeferredSystemInstallAction_WithRollbackAndUninstall_SequencedInExecuteScript()
    {
        using var scratch = new Scratch();

        var appId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var http = new HttpExtension();
        http.AddSniSslBinding("api.example.com", 443, ssl => ssl.Thumbprint(ValidThumbprint).AppId(appId));

        using var db = Compile(scratch, "HttpSslCertApp", http);
        var actions = QueryCustomActions(db);
        var sequence = QuerySequence(db);

        var install = actions["HttpSsl_0"];
        Assert.True((install.Type & InScript) != 0 && (install.Type & NoImpersonate) != 0,
            "sslcert install action must be deferred + SYSTEM");
        string installScript = DecodeEncoded(install.Target);
        Assert.Contains("'http' 'add' 'sslcert'", installScript, StringComparison.Ordinal);
        Assert.Contains("'hostnameport=api.example.com:443'", installScript, StringComparison.Ordinal);
        Assert.Contains($"'certhash={ValidThumbprint}'", installScript, StringComparison.Ordinal);
        Assert.Contains("'appid={aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}'", installScript, StringComparison.Ordinal);
        Assert.Contains("'certstorename=MY'", installScript, StringComparison.Ordinal);

        var rollback = actions["HttpSsl_0_rb"];
        Assert.True((rollback.Type & Rollback) != 0, "sslcert install must have a rollback action");
        Assert.Contains("'http' 'delete' 'sslcert'", DecodeEncoded(rollback.Target), StringComparison.Ordinal);

        var uninstall = actions["HttpSsl_0_un"];
        Assert.Contains("'http' 'delete' 'sslcert'", DecodeEncoded(uninstall.Target), StringComparison.Ordinal);

        int init = sequence["InstallInitialize"];
        int finalize = sequence["InstallFinalize"];
        Assert.InRange(sequence["HttpSsl_0"], init + 1, finalize - 1);
        Assert.True(sequence["HttpSsl_0_rb"] < sequence["HttpSsl_0"], "rollback scheduled before the add");
        Assert.InRange(sequence["HttpSsl_0_un"], init + 1, finalize - 1);
    }

    [Fact]
    public void MixedReservationAndBinding_BothReachTheCompiledMsi_WithDistinctActions()
    {
        using var scratch = new Scratch();

        var http = new HttpExtension();
        http.AddUrlReservation("http://+:8080/svc/", url => url.AllowNetworkService());
        http.AddSniSslBinding("api.example.com", 443, ssl => ssl.Thumbprint(ValidThumbprint));

        using var db = Compile(scratch, "HttpMixedApp", http);
        var actions = QueryCustomActions(db);

        Assert.True(actions.ContainsKey("HttpUrl_0"));
        Assert.True(actions.ContainsKey("HttpSsl_0"));
    }

    [Fact]
    public void OldInertCustomActionAuthoring_IsGone_OneExecutionPathOnly()
    {
        using var scratch = new Scratch();

        var http = new HttpExtension();
        http.AddUrlReservation("http://+:8080/svc/", url => url.AllowNetworkService());
        http.AddSniSslBinding("api.example.com", 443, ssl => ssl.Thumbprint(ValidThumbprint));

        using var db = Compile(scratch, "HttpNoDeadCodeApp", http);
        var actionNames = QueryCustomActions(db).Keys;

        // The former bespoke, unescaped contributor pair (HttpCustomActionContributor /
        // HttpSequenceContributor) named its rows with these exact prefixes. None must survive the
        // migration to the shared execution seam — one live execution path, not two.
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpAddUrlAcl_", StringComparison.Ordinal));
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpRollbackUrlAcl_", StringComparison.Ordinal));
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpRemoveUrlAcl_", StringComparison.Ordinal));
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpAddSslCert_", StringComparison.Ordinal));
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpRollbackSslCert_", StringComparison.Ordinal));
        Assert.DoesNotContain(actionNames, n => n.StartsWith("HttpRemoveSslCert_", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the URL ACL reservation genuinely reaches http.sys on a real per-machine install, and is
    /// removed on uninstall — not merely authored into the MSI. Gated behind <c>FALKFORGE_E2E=1</c> AND
    /// <c>FALKFORGE_REAL_SYSTEM_E2E=1</c> AND administrator elevation, mirroring
    /// <see cref="UtilExecutionEmissionTests.FileShare_IsCreatedThenRemoved_OnRealInstall"/>.
    /// Honestly skips (never a silent fake pass) when a gate is closed (<c>FALKFORGE_REAL_SYSTEM_E2E</c>
    /// is separate from the generic <c>FALKFORGE_E2E</c> opt-in because GitHub-hosted Windows runners
    /// are always elevated).
    /// </summary>
    [Fact]
    public void UrlAcl_IsAddedThenRemoved_OnRealInstall()
    {
        if (Environment.GetEnvironmentVariable("FALKFORGE_E2E") != "1")
            Assert.Skip("Real urlacl install e2e is opt-in: set FALKFORGE_E2E=1 to run it.");
        if (Environment.GetEnvironmentVariable("FALKFORGE_REAL_SYSTEM_E2E") != "1")
            Assert.Skip("Real urlacl install mutates machine-wide state: set FALKFORGE_REAL_SYSTEM_E2E=1 " +
                        "on a machine you own to run it.");
        if (!IsElevated())
            Assert.Skip("Real urlacl install requires administrator elevation; run the test host elevated.");

        string suffix = Guid.NewGuid().ToString("N")[..8];
        // Deterministic pseudo-random port in the dynamic/private range, derived from the GUID suffix, so
        // parallel test runs don't collide on a fixed reservation.
        int port = 40000 + (int)(uint.Parse(suffix[..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) % 5000);
        string url = $"http://+:{port.ToString(CultureInfo.InvariantCulture)}/ffe2e{suffix}/";

        using var scratch = new Scratch();
        var http = new HttpExtension();
        Assert.True(http.AddUrlReservation(url, u => u.AllowEveryone()).Validate().IsSuccess);

        var package = MinimalPackage(scratch, "HttpUrlAclE2eApp");
        var result = new MsiCompiler(new WindowsFileSystem()).Use(http).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
        string msi = result.Value;

        RemoveUrlAclIfPresent(url);
        try
        {
            int install = RunMsiExec($"/i \"{msi}\" /qn /norestart");
            Assert.True(install is 0 or 3010, $"msiexec install exit code {install}");
            Assert.True(UrlAclExists(url), "URL ACL reservation was not created by the deferred action");

            int uninstall = RunMsiExec($"/x \"{msi}\" /qn /norestart");
            Assert.True(uninstall is 0 or 3010, $"msiexec uninstall exit code {uninstall}");
            Assert.False(UrlAclExists(url), "URL ACL reservation was not removed on uninstall");
        }
        finally
        {
            RemoveUrlAclIfPresent(url);
        }
    }

    /// <summary>
    /// The SNI SSL cert binding path is NOT exercised with a real per-machine install: it needs a real
    /// certificate present in the LocalMachine\My store with a known thumbprint before
    /// <c>netsh http add sslcert</c> can succeed. Generating and importing a throwaway self-signed
    /// certificate (and reliably cleaning it back out of the machine store afterward, across CI and local
    /// runs) is materially riskier than the urlacl e2e above and is out of scope for this migration —
    /// honestly skipped rather than faked or attempted with a fragile cert-provisioning shortcut. The
    /// structural test above proves the command is correctly assembled and sequenced; only the real
    /// netsh execution against a live certificate is deferred.
    /// </summary>
    [Fact]
    public void SslCert_RealInstall_IsDeliberatelyNotExercised()
    {
        Assert.Skip(
            "SNI SSL cert e2e requires a real certificate in LocalMachine\\My — deliberately deferred " +
            "(see XML doc). Structural coverage lives in SslCert_ProducesDeferredSystemInstallAction_...");
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static MsiDatabase Compile(Scratch scratch, string name, HttpExtension http)
    {
        var package = MinimalPackage(scratch, name);
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(http).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for http execution emission test");

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

    private static int RunMsiExec(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments) { UseShellExecute = false })!;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static bool UrlAclExists(string url)
        => RunPowerShellSucceeds(
            $"$out = & \\\"$env:SystemRoot\\System32\\netsh.exe\\\" http show urlacl url='{url}'; " +
            "if ($out -match 'Reserved URL') { exit 0 } else { exit 1 }");

    private static void RemoveUrlAclIfPresent(string url)
        => RunPowerShellSucceeds(
            $"& \\\"$env:SystemRoot\\System32\\netsh.exe\\\" http delete urlacl url='{url}' | Out-Null; exit 0");

    private static bool RunPowerShellSucceeds(string command)
    {
        using var process = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"")
        {
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"HttpExecEmit_{Guid.NewGuid():N}");

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
