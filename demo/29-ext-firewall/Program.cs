using FalkForge;
using FalkForge.Compiler.Msi;
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

Console.WriteLine("Firewall: 1 rule configured. Validation runs automatically during compilation.");

// Attach the extension to the compiler with .Use(...). This is what makes its
// WixFirewallException table + rows emit into the compiled MSI.
return Installer.Build(args, package =>
{
    package.Name = "Firewall Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/webapp.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FirewallDemo"));
}, new MsiCompiler().Use(firewall));