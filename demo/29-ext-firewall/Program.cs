using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Extensions.Firewall;

// Configure a Windows Firewall rule to allow inbound HTTP traffic.
var firewall = new FirewallExtension();

firewall.AddRule(rule => rule
    .Id("AllowHttp")
    .Name("My App HTTP")
    .Description("Allow inbound HTTP on port 8080")
    .Protocol(FirewallProtocol.Tcp)
    .Port("8080")
    .Direction(FirewallDirection.Inbound)
    .Action(FirewallRuleAction.Allow)
    .Profile(FirewallProfile.All));

var errors = firewall.ValidateRules();
if (errors.Count > 0)
{
    foreach (var e in errors)
        Console.Error.WriteLine($"{e.Code}: {e.Message}");
    return 1;
}

Console.WriteLine($"Firewall: {errors.Count} errors, 1 rule configured.");

// In production, extensions register automatically via the FalkForge SDK extension
// pipeline during compilation. The package below shows the MSI structure; extension
// tables are emitted by the SDK at build time.
return Installer.Build(args, package =>
{
    package.Name = "Firewall Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/webapp.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FirewallDemo"));

}, new MsiCompiler());
