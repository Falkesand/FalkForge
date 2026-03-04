# Demo 29: Windows Firewall Extension

Configures Windows Firewall rules as part of an MSI installer. This demo creates an inbound TCP rule that opens port 8080 for HTTP traffic, using the fluent `FirewallExtension` API with built-in validation.

## What This Demonstrates

- Creating a `FirewallExtension` instance and adding firewall rules
- Fluent builder pattern for configuring protocol, port, direction, action, and profile
- Validating rules before compilation with `ValidateRules()`
- Integrating the extension into a standard FalkForge installer package

## Key API Calls

```csharp
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
```

## How to Build

```shell
dotnet build demo/29-ext-firewall/29-ext-firewall.csproj
```

## Notes

- The firewall rule is created during MSI installation and removed on uninstall.
- `FirewallProfile.All` applies the rule to Domain, Private, and Public network profiles.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation. This demo shows the MSI structure; extension tables are emitted by the SDK at build time.
