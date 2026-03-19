# Demo 30: IIS Extension

Configures an IIS application pool and web site as part of an MSI installer. This demo creates an integrated-pipeline
app pool running under `ApplicationPoolIdentity` and binds a web site to port 8080 with auto-start enabled.

## What This Demonstrates

- Creating an `IisExtension` instance with app pool and web site definitions
- Fluent builder for application pool settings (managed code mode, pipeline, identity)
- Fluent builder for web site settings (directory, binding, auto-start)
- Linking a web site to an app pool via the `AppPool()` reference
- Validating the full IIS configuration with `Validate()` which returns a `Result` type

## Key API Calls

```csharp
var iis = new IisExtension();

var appPool = iis.DefineAppPool(pool => pool
    .Id("DemoPool")
    .Name("DemoPool")
    .NoManagedCode()
    .PipelineMode(ManagedPipelineMode.Integrated)
    .Identity(AppPoolIdentityType.ApplicationPoolIdentity));

iis.AddWebSite(site => site
    .Id("DemoSite")
    .Description("Demo Web Site")
    .Directory("[INSTALLDIR]wwwroot")
    .AppPool(appPool)
    .Binding(8080, "http")
    .AutoStart(true));

var validation = iis.Validate();
```

## How to Build

```shell
dotnet build demo/30-ext-iis/30-ext-iis.csproj
```

## Notes

- `NoManagedCode()` configures the app pool with no CLR version, suitable for hosting reverse-proxy or static-file
  scenarios.
- The `Directory` value uses the `[INSTALLDIR]` MSI property to resolve the physical path at install time.
- `DefineAppPool` returns a reference object that can be passed to `AppPool()` on the web site builder, ensuring
  referential integrity.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation.
