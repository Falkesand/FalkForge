# Demo 34: Dependency Extension (Provides/Requires)

Declares inter-package dependency relationships using the Windows Installer dependency provider/consumer model. This
demo registers the current package as a provider of a shared runtime component and simultaneously declares a requirement
on another package, enabling safe uninstall and upgrade ordering.

## What This Demonstrates

- Creating a `DependencyExtension` instance for package dependency management
- Registering as a dependency provider with `Provides()` (version and display name)
- Declaring a dependency requirement with `Requires()` (consumer key, minimum version, version range)
- Validating dependency declarations with `ValidateDependencies()`
- Preventing uninstall of shared packages while dependents are still installed

## Key API Calls

```csharp
var dependency = new DependencyExtension();

// Register as a provider
dependency.Provides("Demo.SharedRuntime", provider => provider
    .Version("1.0.0")
    .DisplayName("Demo Shared Runtime"));

// Declare a requirement
dependency.Requires("Demo.Framework", consumer => consumer
    .ConsumerKey("Demo.App")
    .MinVersion("2.0.0")
    .MinInclusive());

var errors = dependency.ValidateDependencies();
```

## How to Build

```shell
dotnet build demo/34-ext-dependency/34-ext-dependency.csproj
```

## Notes

- The provider key (e.g., `"Demo.SharedRuntime"`) is a stable identifier shared across packages. Other installers
  reference this key in their `Requires` declarations.
- `MinInclusive()` means version `2.0.0` itself satisfies the requirement. Without it, only versions strictly greater
  than `2.0.0` would match.
- The dependency system prevents uninstalling a provider package while any consumer still references it, avoiding broken
  installations.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation.
