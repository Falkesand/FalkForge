using System.Text;
using FalkForge.Extensions.Http.Models;
using Xunit;

namespace FalkForge.Extensions.Http.Tests;

/// <summary>
/// Command-generation tests for <see cref="HttpCommandFactory"/>. The generated commands run in a
/// deferred, elevated (SYSTEM) custom action, so injection safety is a privilege-boundary concern: a URL,
/// user/SDDL string, hostname, or cert store name is untrusted input and must never be able to inject
/// additional PowerShell statements or netsh arguments. Commands are emitted as
/// <c>powershell.exe -EncodedCommand &lt;base64&gt;</c>; the tests decode the base64 back to the script and
/// assert on that.
/// </summary>
public sealed class HttpCommandFactoryTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    private static UrlReservationModel Reservation(string url = "http://+:8080/svc/", string user = "D:(A;;GX;;;NS)")
        => new() { Url = url, User = user };

    private static SniSslBindingModel Binding(
        string hostname = "api.example.com",
        int port = 443,
        string thumbprint = ValidThumbprint,
        Guid appId = default,
        string certStoreName = "MY")
        => new()
        {
            Hostname = hostname,
            Port = port,
            CertificateThumbprint = thumbprint,
            AppId = appId == Guid.Empty ? Guid.Parse("11111111-2222-3333-4444-555555555555") : appId,
            CertStoreName = certStoreName,
        };

    private static string DecodeScript(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"command is not an -EncodedCommand invocation: {command}");
        string base64 = command[(idx + marker.Length)..].Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    // ── URL ACL ──────────────────────────────────────────────────────────────

    [Fact]
    public void UrlReservation_ProducesAddInstallAndDeleteRollbackUninstall()
    {
        var steps = HttpCommandFactory.BuildUrlAclSteps([Reservation()]);

        var step = Assert.Single(steps);
        Assert.Equal("HttpUrl_0", step.Id);

        var install = DecodeScript(step.InstallCommand);
        Assert.Contains("'http' 'add' 'urlacl'", install, StringComparison.Ordinal);
        Assert.Contains("'url=http://+:8080/svc/'", install, StringComparison.Ordinal);
        Assert.Contains("'user=D:(A;;GX;;;NS)'", install, StringComparison.Ordinal);
        Assert.EndsWith("exit $LASTEXITCODE", install, StringComparison.Ordinal);

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        string rollback = DecodeScript(step.RollbackCommand!);
        string uninstall = DecodeScript(step.UninstallCommand!);
        Assert.Contains("'http' 'delete' 'urlacl'", rollback, StringComparison.Ordinal);
        Assert.Contains("'url=http://+:8080/svc/'", rollback, StringComparison.Ordinal);
        Assert.Equal(rollback, uninstall);
    }

    [Fact]
    public void DeleteScripts_AlwaysExitZero_SoRemovingAnAbsentReservationDoesNotFailTheTransaction()
    {
        var step = HttpCommandFactory.BuildUrlAclSteps([Reservation()])[0];

        Assert.EndsWith("exit 0", DecodeScript(step.RollbackCommand!), StringComparison.Ordinal);
        Assert.EndsWith("exit 0", DecodeScript(step.UninstallCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedCommand_TransportCarriesNoQuoteOrShellMetacharacters()
    {
        // The whole point of -EncodedCommand: the emitted Target has no quote, bracket or space beyond
        // the fixed prefix, so nothing a crafted url/user value contains can break out of the command
        // line or trigger MSI Formatted substitution. The base64 payload is [A-Za-z0-9+/=] only.
        var steps = HttpCommandFactory.BuildUrlAclSteps([Reservation(url: "http://+:8080/a'\"; calc; [X]/")]);
        string command = steps[0].InstallCommand;

        const string marker = "-EncodedCommand ";
        string payload = command[(command.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        Assert.DoesNotContain('"', payload);
        Assert.DoesNotContain('\'', payload);
        Assert.DoesNotContain('[', payload);
        Assert.DoesNotContain(';', payload);
        Assert.Matches("^[A-Za-z0-9+/=]+$", payload);
    }

    [Fact]
    public void SingleQuoteInUrl_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "http://+:8080/a'; Start-Process calc.exe; '/";
        var steps = HttpCommandFactory.BuildUrlAclSteps([Reservation(url: malicious)]);

        var install = DecodeScript(steps[0].InstallCommand);

        // The value is wrapped in a single-quoted literal with every ' doubled → inert.
        Assert.Contains("'url=http://+:8080/a''; Start-Process calc.exe; ''/'", install, StringComparison.Ordinal);

        // The literal must never terminate early (a bare, non-doubled quote right after "a" would hand
        // control back to the PowerShell parser, exposing "; Start-Process calc.exe; " as a statement).
        Assert.DoesNotContain("'url=http://+:8080/a'; Start-Process", install, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleQuoteInUser_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "D:(A;;GX;;;NS)'; Remove-Item C:\\ -Recurse -Force; '";
        var steps = HttpCommandFactory.BuildUrlAclSteps([Reservation(user: malicious)]);

        var install = DecodeScript(steps[0].InstallCommand);

        // The literal must remain a single continuous quoted token — no bare (unquoted) occurrence of the
        // injected statement exists in the decoded script.
        Assert.Contains("'user=D:(A;;GX;;;NS)''; Remove-Item C:\\ -Recurse -Force; '''", install, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        var install = HttpCommandFactory.BuildUrlAclSteps([Reservation()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
        Assert.DoesNotContain("\"powershell.exe", install, StringComparison.Ordinal);
    }

    [Fact]
    public void NetshIsResolvedViaTrustedSystemRoot_NotABareNetshExe()
    {
        var install = DecodeScript(HttpCommandFactory.BuildUrlAclSteps([Reservation()])[0].InstallCommand);
        Assert.Contains("$env:SystemRoot\\System32\\netsh.exe", install, StringComparison.Ordinal);
        Assert.DoesNotContain("& \"netsh.exe\"", install, StringComparison.Ordinal);
        Assert.DoesNotContain("& netsh.exe", install, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoReservations_ProduceDistinctStepIds()
    {
        var steps = HttpCommandFactory.BuildUrlAclSteps(
        [
            Reservation(url: "http://+:8080/a/"),
            Reservation(url: "http://+:9090/b/"),
        ]);

        Assert.Equal(["HttpUrl_0", "HttpUrl_1"], steps.Select(s => s.Id));
    }

    // ── SNI SSL cert binding ─────────────────────────────────────────────────

    [Fact]
    public void SslBinding_ProducesAddInstallAndDeleteRollbackUninstall()
    {
        var appId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(appId: appId)]);

        var step = Assert.Single(steps);
        Assert.Equal("HttpSsl_0", step.Id);

        var install = DecodeScript(step.InstallCommand);
        Assert.Contains("'http' 'add' 'sslcert'", install, StringComparison.Ordinal);
        Assert.Contains("'hostnameport=api.example.com:443'", install, StringComparison.Ordinal);
        Assert.Contains($"'certhash={ValidThumbprint}'", install, StringComparison.Ordinal);
        Assert.Contains("'appid={aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}'", install, StringComparison.Ordinal);
        Assert.Contains("'certstorename=MY'", install, StringComparison.Ordinal);
        Assert.EndsWith("exit $LASTEXITCODE", install, StringComparison.Ordinal);

        var rollback = DecodeScript(step.RollbackCommand!);
        var uninstall = DecodeScript(step.UninstallCommand!);
        Assert.Contains("'http' 'delete' 'sslcert'", rollback, StringComparison.Ordinal);
        Assert.Contains("'hostnameport=api.example.com:443'", rollback, StringComparison.Ordinal);
        Assert.Equal(rollback, uninstall);
    }

    [Fact]
    public void SslBinding_CustomCertStoreName_IsInCommand()
    {
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(certStoreName: "WebHosting")]);
        var install = DecodeScript(steps[0].InstallCommand);

        Assert.Contains("'certstorename=WebHosting'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void SslBinding_MaliciousCertStoreName_IsQuotedSafely()
    {
        var malicious = "MY'; Start-Process calc.exe; '";
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(certStoreName: malicious)]);
        var install = DecodeScript(steps[0].InstallCommand);

        Assert.Contains("'certstorename=MY''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);
    }

    [Fact]
    public void SslBinding_Ipv6Hostname_IsBracketed()
    {
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(hostname: "::1")]);
        var install = DecodeScript(steps[0].InstallCommand);

        Assert.Contains("'hostnameport=[::1]:443'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void SslBinding_AlreadyBracketedIpv6Hostname_IsNotDoubleBracketed()
    {
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(hostname: "[::1]")]);
        var install = DecodeScript(steps[0].InstallCommand);

        Assert.Contains("'hostnameport=[::1]:443'", install, StringComparison.Ordinal);
        Assert.DoesNotContain("[[::1]]", install, StringComparison.Ordinal);
    }

    [Fact]
    public void SslBinding_DnsHostname_IsNotBracketed()
    {
        var steps = HttpCommandFactory.BuildSslCertSteps([Binding(hostname: "api.example.com")]);
        var install = DecodeScript(steps[0].InstallCommand);

        Assert.Contains("'hostnameport=api.example.com:443'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoBindings_ProduceDistinctStepIds()
    {
        var steps = HttpCommandFactory.BuildSslCertSteps(
        [
            Binding(hostname: "a.example.com"),
            Binding(hostname: "b.example.com"),
        ]);

        Assert.Equal(["HttpSsl_0", "HttpSsl_1"], steps.Select(s => s.Id));
    }

    [Fact]
    public void EmptyReservationsAndBindings_ProduceNoSteps()
    {
        Assert.Empty(HttpCommandFactory.BuildUrlAclSteps([]));
        Assert.Empty(HttpCommandFactory.BuildSslCertSteps([]));
    }
}
