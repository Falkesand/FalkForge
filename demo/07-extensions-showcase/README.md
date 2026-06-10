# Demo 07: Extensions Showcase

Demonstrates all five FalkForge extension APIs in a single project: Firewall, IIS, SQL, .NET Detection, and Util (XmlConfig + QuietExec).

## What This Demonstrates

- **Firewall**: Two TCP rules (inbound port 8080, outbound port 1433) using `FirewallExtension` with `Direction`, `Action`, and `Profile`
- **IIS**: `IisExtension` with `DefineAppPool()` (typed `AppPoolRef`) and `AddWebSite()` with HTTP binding, idle timeout, and recycle interval
- **SQL**: `SqlExtension` with `DefineDatabase()` (typed `SqlDatabaseRef`) returning `Result<T>`, plus `SqlScriptBuilder` for schema scripts
- **.NET Detection**: `DotNetExtension` with `DotNetCoreSearchBuilder` detecting .NET 8.0+ x64 Runtime, storing result in a named variable
- **Util / XmlConfig**: Two `XmlConfigBuilder` entries transforming `web.config` at install time via XPath attribute setting
- **Util / QuietExec**: `QuietExecModel` defining a silent post-install command
- `Result<T>` pattern for extension builder errors (all extensions surface failures as `Result<T>`)
- `InstallScope.PerMachine` and `ProcessorArchitecture.X64` on the package

## Key API Calls

```csharp
// Firewall extension
var firewall = new FirewallExtension();
firewall.AddRule(rule => rule
    .Id("AllowHttp8080")
    .Protocol(FirewallProtocol.Tcp)
    .Port("8080")
    .Direction(FirewallDirection.Inbound)
    .Action(FirewallRuleAction.Allow)
    .Profile(FirewallProfile.All));

// IIS extension — typed AppPoolRef
var iis = new IisExtension();
var appPool = iis.DefineAppPool(pool => pool
    .Id("ShowcaseAppPool")
    .NoManagedCode()
    .PipelineMode(ManagedPipelineMode.Integrated)
    .Identity(AppPoolIdentityType.ApplicationPoolIdentity));

iis.AddWebSite(site => site
    .Id("ShowcaseWebSite")
    .Directory("[INSTALLDIR]wwwroot")
    .AppPool(appPool)
    .Binding(8080, "http"));

// SQL extension — Result<T> pattern
var sql = new SqlExtension();
var dbRef = sql.DefineDatabase(db => db
    .Id("AppDb")
    .Server("[SQLSERVER]")
    .Database("ExtShowcaseDb")
    .CreateOnInstall()
    .ConfirmOverwrite());

var scriptResult = new SqlScriptBuilder()
    .Id("CreateTables")
    .Database(dbRef.Value)
    .SourceFile("payload\\create-tables.sql")
    .ExecuteOnInstall()
    .Sequence(1)
    .Build();

// .NET detection
var searchResult = new DotNetCoreSearchBuilder()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .Platform(DotNetPlatform.X64)
    .MinVersion(new Version(8, 0, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

// XmlConfig
var connStringConfig = new XmlConfigBuilder()
    .Id("SetConnectionString")
    .File("[INSTALLDIR]web.config")
    .XPath("//connectionStrings/add[@name='AppDb']")
    .SetAttribute("connectionString", "Server=[SQLSERVER];Database=ExtShowcaseDb;Trusted_Connection=True;")
    .Sequence(1)
    .Build();
```

## How to Build

```bash
dotnet build demo/07-extensions-showcase/
```

## How to Run

Produces a `.msi` file. Requires Windows with `msi.dll`.

```bash
dotnet run --project demo/07-extensions-showcase/ -- -o ./output
```

## Notes

- `DefineDatabase()` and `DefineAppPool()` return typed reference objects (`SqlDatabaseRef`, `AppPoolRef`) that other builders accept as arguments, enforcing cross-reference correctness at compile time.
- `DotNetCoreSearchBuilder.Variable()` names the MSI property set to the detected runtime path. Use this property in a launch condition to block installation when the runtime is absent.
- `XmlConfigBuilder.XPath()` targets a specific XML element by attribute value. The `SetAttribute()` call replaces the named attribute with an MSI property-expanded value at install time.
- `QuietExecModel` runs a command with no visible console window; the installer fails if the command returns a non-zero exit code.
