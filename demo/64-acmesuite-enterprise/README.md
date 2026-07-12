# Demo 64: AcmeSuite Enterprise (Capstone)

The **"put it all together"** walkthrough. Every other demo isolates a single capability so you can
learn it in isolation. This one is the opposite: it composes the whole enterprise stack the way a
real product ships it, so you can see the pieces working *together* — a feature tree, a gated
service, IIS and SQL provisioning with secure credentials, a correctly-scheduled custom action,
shortcuts and a file association, all rolled into a signed, auto-updating bundle.

If you have worked through the focused demos (16 features, 17 services, 30 IIS, 31 SQL, 20 custom
actions, 59–62 signing) this demo is where those threads meet.

## What This Demonstrates

One `dotnet run` produces two artifacts:

- `AcmeSuite Enterprise.msi` — the composed product installer
- `AcmeSuite Enterprise Setup.exe` — the signed, auto-updating bundle wrapping it (plus a
  prerequisite runtime MSI)

Composed from these existing FalkForge APIs:

| Capability | API | Why it is here |
|---|---|---|
| Feature tree | `PackageBuilder.Feature(...)` (Server / Client / Tools) | Let the user choose which tiers to install; each tier owns its own files and configuration. |
| Feature-gated service | `FeatureBuilder.Service(...)` | The service belongs to the Server tier. FalkForge stamps its synthesized component under the `Server` feature, so it is laid down **only** when Server is selected — no orphaned service if a client-only install is chosen. |
| Feature-gated registry | `FeatureBuilder.Registry(...)` + package-level `Registry(...)` | Shared product keys always install; Server/Tools keys ride with their feature. |
| IIS app pool + site (secure) | `IisExtension` + `AppPoolBuilder.IdentitySecure(...)` | Provision a web site whose app pool runs as a specific user, with the password supplied **securely at run time** via an MSI property — never written into the MSI. |
| SQL database + script (secure) | `SqlExtension` + `SqlDatabaseBuilder.PasswordProperty(...)` | Create the database and run a schema script, authenticating with a SQL login whose password also flows through a secure property. |
| Multi-secret aggregation | (emergent) | Using **both** the IIS secret and the SQL secret in one package aggregates every secret property name into a single `MsiHiddenProperties` row (see below). |
| Custom action | `CustomAction(...)` + `ExecuteSequence(...)` | Set a deployment-tier property, scheduled the **only** way the compiler honours — through `ExecuteSequence`. |
| Shortcuts + file association | `FeatureBuilder.Shortcut(...)` / `FeatureBuilder.FileAssociation(...)` | A Start-menu launcher and a `.acme` document association, both under the Client feature. |
| Signed, updatable bundle | `BundleBuilder.Integrity(...)` + `.UpdateFeed(...)` | Wrap the prerequisite + product MSIs into one EXE, sign the payload (ECDSA integrity), and point it at an update feed. |

## The Multi-Secret Proof (why this demo builds at all)

The IIS app-pool password and the SQL login password are **both** secrets that must not be stored
in the MSI. FalkForge routes each through the deferred-custom-action `CustomActionData` channel and
records its property name in the MSI's `MsiHiddenProperties` list, so a verbose `msiexec /L*v` log
redacts the value instead of leaking it.

`MsiHiddenProperties` is a single `Property` row (its primary key is the literal string
`"MsiHiddenProperties"`). Before the aggregation fix, **each** secret-bearing extension authored its
own `MsiHiddenProperties` row — so a package using two of them (exactly this composition) produced
two rows with the same primary key and the build failed with a duplicate-PK error. The fix
aggregates every extension's secret property names into **one** deterministic row.

So the headline is subtle but real: **the fact that this multi-secret package builds at all is the
proof the fix works.** The always-on test
`tests/FalkForge.Integration.Tests/AcmeSuiteEnterpriseCompositionTests.cs` compiles this exact
composition and asserts the single aggregated row lists both the SQL and the IIS secret property
names (and their `CustomActionData` carriers), deterministically sorted.

## How to Run

```
dotnet run --project demo/64-acmesuite-enterprise --output ./out
```

Prints the MSI path, confirms the bundle's integrity signature is present and verifies, echoes the
configured update feed URL, and writes both artifacts into `./out`.

> Tip: pass `--output <dir>` (long form). When launched through `dotnet run`, the short `-o` flag is
> consumed by `dotnet run` itself; the built executable accepts both.

## Key API Calls

```csharp
// IIS: secure app-pool identity (password from the IISAPPPOOLPWD property, never in the MSI)
var iis = new IisExtension();
var pool = iis.DefineAppPool(p => p
    .Id("AcmePool").Name("AcmePool")
    .IdentitySecure(AppPoolIdentityType.SpecificUser, "ACME\\svcweb", "IISAPPPOOLPWD"));
iis.AddWebSite(s => s.Id("AcmeSite").Directory("[INSTALLFOLDER]Server\\wwwroot").AppPool(pool).Binding(80));

// SQL: secure login (password from the SQLPASSWORD property)
var sql = new SqlExtension();
var db = sql.DefineDatabase(d => d
    .Id("AcmeDb").Server(".").Database("AcmeSuite").CreateOnInstall()
    .User("acme_app").PasswordProperty("SQLPASSWORD"));

// Windows service gated to the Server feature
builder.Feature("Server", server => server.Service("AcmeServer", svc =>
{
    svc.Executable = @"[ProgramFilesFolder]Acme Corporation\AcmeSuite\Server\AcmeServer.exe";
    svc.StartMode = ServiceStartMode.Automatic;
}));

// Custom action scheduled the ONLY correct way
builder.CustomAction("SetDeploymentTier", ca => ca.SetProperty("ACMEDEPLOYTIER", "Enterprise"));
builder.ExecuteSequence(seq => seq.Action("SetDeploymentTier").After("CostFinalize"));

// One .Use(...) attaches both extensions; this is where multi-secret aggregation happens
new MsiCompiler(new WindowsFileSystem()).Use(iis, sql).Compile(package, outputDir);

// Signed, auto-updating bundle
var bundle = new BundleBuilder()
    .Name("AcmeSuite Enterprise Setup")
    .Integrity(i => { })  // ephemeral ECDSA key; use i.SigningKey("key.pem") for a stable release key
    .UpdateFeed("https://updates.acme.example/acmesuite/feed.json", UpdatePolicy.DownloadAndPrompt)
    .Chain(c => c.MsiPackage(prereqMsi, p => p.Id("AcmeRuntime")).MsiPackage(mainMsi, p => p.Id("AcmeSuite")))
    .Build();
```

## Honest Scope

- `dotnet run` **builds** the packages. Actually creating the IIS site, the SQL database and the
  Windows service happens when the MSI is installed on a machine with **administrator rights, IIS and
  SQL Server present**. The composition-and-signing proof is what this demo (and its test) guarantees.
- The IIS **HTTPS / certificate binding is configuration-only** today — certificate provisioning is a
  documented follow-up in the IIS extension — so it is intentionally left out here rather than
  declared as if it fully provisioned a certificate at runtime.
- The secure passwords are supplied at run time by a custom-UI installer via `SetSecureProperty`
  (see demo 14 for that pattern). They are declared here as empty, public, secure properties.

## Where to Go Next

- The underlying MSI mechanics (components, features, sequences, custom actions) are explained in the
  manual's **Concepts** section — see the full manual at [`../../documentation.html`](../../documentation.html)
  and the [MSI Basics](../../docs/tutorials/msi-basics.html) tutorial.
- Signing and update trust: demos **59** (integrity signing), **60** (trusted-key rotation),
  **62** (require-signed updates).
- The individual capabilities in isolation: **16** (features), **17** (services), **20** (custom
  actions), **30** (IIS), **31** (SQL), **19** (file associations).
