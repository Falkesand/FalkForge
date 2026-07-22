# Real-Machine Verification Runbook — beta.3/beta.4 Live Paths

FalkForge's default `dotnet test` run never touches real machine state (no firewall rules, no
IIS sites, no SQL databases, no elevation). A handful of features shipped in 0.5.0-beta.3 are
built and unit-tested but have never been driven end-to-end on a real elevated Windows machine:

- Bundle chain install + rollback boundaries (isolating a failed package from the rest of the
  chain)
- IIS install-time actions: app pool + web site creation, virtual directories, and HTTPS
  certificate binding
- The per-package feature picker actually filtering installed files via `ADDLOCAL`
- External/downloadable bundle containers (`Container.DownloadUrl`): live download, verify, and
  install
- The turnkey built-in UI host (`FalkForge.Ui.exe`) actually showing a window when the engine
  spawns it
- Per-culture install UI (localized wizard strings)
- The Authenticode `forge bundle detach` → `signtool` → `forge bundle reattach` signing ceremony

This runbook is a step-by-step guide to verify all of the above on a disposable, elevated
Windows VM. Part 1 runs the *automated* real-system e2e suite that already exists in the repo.
Part 2 is a manual checklist for the parts that suite does **not** cover — be honest with
yourself about the difference: a green automated run proves less than the full gap list above.

## Automated vs. manual — the honest map

| Gap | Automated real-system test? | Where |
|---|---|---|
| Firewall rule create/remove | **Yes** | `ExecutionStepEmissionTests.FirewallRule_IsCreatedThenRemoved_OnRealInstall` |
| Dependency version-range gate | **Yes** | `DependencyVersionEnforcementTests.VersionRangeCheck_BlocksRealInstall_WhenProviderMissingOrOutOfRange_AllowsWhenInRange` |
| IIS app pool + web site create/remove | **Yes** | `IisExecutionEmissionTests.PoolAndSite_AreCreated_ThenRemoved_OnRealInstall` |
| IIS virtual directory create/remove | **Yes** | `IisExecutionEmissionTests.VirtualDirectory_IsCreated_ThenRemoved_OnRealInstall` |
| IIS HTTPS certificate binding | **No** (compile/structure-only unit test; no real-install test exists) | manual — Part 2.2 |
| HTTP URL ACL reservation | **Yes** | `HttpExecutionEmissionTests.UrlAcl_IsAddedThenRemoved_OnRealInstall` |
| SQL database create + script run/drop | **Yes** | `SqlExecutionEmissionTests.Database_IsCreatedScriptRun_ThenDropped_OnRealInstall` |
| Local user/group create/remove | **Yes** | `UtilUserGroupExecutionEmissionTests.UserGroupMembership_AreCreatedThenRemoved_OnRealInstall` |
| SMB file share create/remove | **Yes** | `UtilExecutionEmissionTests.FileShare_IsCreatedThenRemoved_OnRealInstall` |
| RemoveFolderEx on uninstall | **Yes** | `UtilExecutionEmissionTests.RemoveFolderEx_DeletesRealFolder_OnUninstall` |
| Bundle chain install + rollback boundaries | **No** | manual — Part 2.1 |
| Per-package feature picker / `ADDLOCAL` filtering | **No** (unit-tested against the view-model and planner only) | manual — Part 2.3 |
| External container download + verify + install | **No** (unit-tested with an in-process fake HTTP seam only — see `ExternalContainerAcquirerTests`, which says so explicitly: "the genuine live-network download is out of scope for a unit test") | manual — Part 2.4 |
| Turnkey UI host shows a window | **No** (`BuiltInUiHostTests` only covers argument parsing, never spawns the process or renders a window) | manual — Part 2.5 |
| Per-culture install UI | **No** | manual — Part 2.6 |
| Authenticode detach/sign/reattach ceremony | **No** (`BundleDetachSignRoundTripTests` signs with a self-signed cert entirely in-process and deliberately stops at `CERT_E_UNTRUSTEDROOT` — it never shells out to `signtool.exe`) | manual — Part 2.7 |
| Composed enterprise bundle (IIS + SQL + service) real install | **No** — `AcmeSuiteEnterpriseCompositionTests.AcmeSuite_RealInstall_CreatesIisSiteSqlDbAndService` is an **unconditional** `Assert.Skip`, not gated on any env var. It documents the gap; it can never pass in any configuration. | manual (compose from Part 2.2/2.3 checks) |

The eight "Yes" rows all live in one project: `tests/FalkForge.Compiler.Msi.Tests`, under
`Recipe/*ExecutionEmissionTests.cs` and `Recipe/DependencyVersionEnforcementTests.cs`. Every one
of them is gated the same way (see any of those files' XML doc comments):
`FALKFORGE_E2E=1` **AND** `FALKFORGE_REAL_SYSTEM_E2E=1` **AND** the test host must be
`IsElevated()` — checked in that order, each with its own honest `Assert.Skip` message if not
met. `FALKFORGE_REAL_SYSTEM_E2E` is deliberately separate from `FALKFORGE_E2E` because
GitHub-hosted Windows runners run with UAC disabled (so `IsElevated()` is always true there) —
elevation alone can't tell CI apart from an operator's prepared machine, hence the second,
CI-never-sets-this opt-in.

## Part 0 — VM setup

Use a disposable Windows VM you're willing to have firewall rules, IIS sites, SQL databases,
local users, and file shares created and removed on. Do not run this against a machine you care
about.

1. **OS + SDK.** Windows 10/11 or Windows Server, .NET SDK 10.0.103+. FalkForge's MSI compiler
   P/Invokes `msi.dll` directly — there is no WiX toolset or `darice.cub` dependency to install.

   ```powershell
   winget install Microsoft.DotNet.SDK.10
   ```

2. **Enable IIS** (needed for the IIS app-pool/site/vdir tests and the AcmeSuite-style manual
   check):

   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-ManagementConsole -All
   ```

   Verify: `Get-Service W3SVC` should show `Running`. The real-system IIS tests probe for this
   themselves (`IisInstalled()` checks for the `W3SVC` service) and self-skip if it's absent.

3. **A reachable SQL Server** for the SQL extension test (probed via integrated auth):

   ```powershell
   winget install Microsoft.SQLServer.2022.Developer
   # or, faster: winget install Microsoft.SQLServer.2022.LocalDB
   ```

4. **A self-signed code-signing certificate** — used for both the IIS HTTPS binding manual check
   (Part 2.2) and the Authenticode ceremony (Part 2.7):

   ```powershell
   $codeSignCert = New-SelfSignedCertificate `
       -Type CodeSigningCert `
       -Subject "CN=FalkForge Verification" `
       -CertStoreLocation Cert:\CurrentUser\My `
       -KeyUsage DigitalSignature `
       -KeyAlgorithm RSA -KeyLength 2048 `
       -NotAfter (Get-Date).AddYears(1)

   $serverCert = New-SelfSignedCertificate `
       -Type SSLServerAuthentication `
       -Subject "CN=falkforge-verify.local" `
       -CertStoreLocation Cert:\LocalMachine\My `
       -KeyAlgorithm RSA -KeyLength 2048 `
       -NotAfter (Get-Date).AddYears(1)
   ```

   `signtool.exe` ships with the Windows SDK — install it if missing:

   ```powershell
   winget install Microsoft.WindowsSDK
   ```

5. **Clone + build the branch under test, and build the `forge` CLI:**

   ```powershell
   git clone https://github.com/Falkesand/FalkForge.git
   cd FalkForge
   git checkout release/0.5.0-beta.4   # or whatever branch/tag you're verifying
   dotnet build FalkForge.slnx
   dotnet build src/FalkForge.Cli/FalkForge.Cli.csproj -c Release
   ```

   Add the built `forge` CLI to your session `PATH`, or invoke it via
   `dotnet run --project src/FalkForge.Cli -- <args>` throughout this runbook.

## Part 1 — Run the automated real-system e2e suite

Open an **elevated** PowerShell (Run as Administrator), from the repo root:

```powershell
$env:FALKFORGE_E2E = '1'
$env:FALKFORGE_REAL_SYSTEM_E2E = '1'
dotnet test tests/FalkForge.Compiler.Msi.Tests/FalkForge.Compiler.Msi.Tests.csproj -c Release -v minimal -- --report-trx --report-trx-filename real-system-e2e.trx
```

This scopes the run to the one project that holds all eight real-system tests (see the table
above) instead of the full ~8,000-test solution, so it stays fast. To run the *entire* heavyweight
e2e surface the same way CI does (full demo-catalog builds, `forge verify --rebuild`, plus these
real-system tests), run the whole solution instead:

```powershell
$env:FALKFORGE_E2E = '1'
$env:FALKFORGE_REAL_SYSTEM_E2E = '1'
dotnet test FalkForge.slnx -c Release -v minimal -- --report-trx --report-trx-filename full-e2e.trx
```

**Reading results:** each of the 8 tests self-skips (not fails) with a descriptive message if a
prerequisite is missing — e.g. `IisExecutionEmissionTests` skips with "Real IIS install requires
administrator elevation" or similar if IIS isn't present. A `Skipped` outcome on this run means
the gate didn't fully open (check elevation, `FALKFORGE_REAL_SYSTEM_E2E`, and — for the IIS and
SQL tests — that IIS/SQL are actually installed), not that the feature is broken. Only a `Failed`
outcome means something is actually wrong. `scripts/verify-real-machine.ps1` already parses the
TRX and prints a Passed/Failed/Skipped summary for you; if you ran `dotnet test` directly instead,
open the TRX in Visual Studio's Test Explorer (or any TRX viewer) for a readable summary instead of
raw XML.

This part only exercises MSI-recipe extension actions (Firewall, IIS pool/site/vdir, HTTP URL
ACL, SQL, User/Group, FileShare). It does **not** touch anything bundle-level — Part 2 covers the
rest.

## Part 2 — Manual verification checklist

None of the following has automated real-machine coverage (see the table above for why). Do
each of these by hand on the same elevated VM.

### 2.1 — Bundle chain install + rollback boundaries

Demo: `demo/41-bundle-rollback`. Two `MsiPackage`s in the chain, each in its own
`RollbackBoundary` ("Prerequisites", "Application").

```powershell
dotnet build demo/41-bundle-rollback/41-bundle-rollback.csproj
dotnet run --project demo/41-bundle-rollback -- -o out
```

Run `out\Rollback Boundaries Bundle-1.0.0.exe` elevated. It should install cleanly (both MSIs
succeed). Confirm via `Get-Package "My Application"` / `Get-Package "Runtime Prerequisites"` that
both products are present. Uninstall through Programs & Features to leave the machine clean.

To prove the *rollback scoping* itself — the actual live-verify gap — force the second package
to fail: replace `demo/41-bundle-rollback/MyApp.msi` with a corrupt/invalid MSI (e.g.
`Set-Content MyApp.msi -Value "not an msi" -Encoding Byte`), rebuild, and run the bundle exe
elevated again. Expect:

- The bundle reports the `MyApp` package failed.
- `Get-Package "Runtime Prerequisites"` still shows installed (it's in the earlier, separate
  rollback boundary — not rolled back).
- `Get-Package "My Application"` shows **not** installed (its own boundary rolled back cleanly,
  no half-installed leftovers under `Get-ChildItem "$env:ProgramFiles\Demo"`).

Clean up: uninstall Runtime Prerequisites via Programs & Features afterward.

### 2.2 — IIS install-time actions: certificate binding

The automated suite proves app-pool/site creation and virtual directories. It does **not**
exercise HTTPS certificate binding on a real install (`IisExecutionEmissionTests` has no
`OnRealInstall` test for it — only a compile/structure unit test,
`HttpsBindingWithCertificate_BindsCertificateHash_InCompiledSiteAction`).

Using the `IisExtension` API and the server cert created in Part 0 step 4, build a minimal MSI
(demo `demo/30-ext-iis` is the closest starting point — adapt its `Program.cs` to add
`.Binding(b => b.Port(443).Certificate(certRef))` with a cert reference resolvable to your
`$serverCert` thumbprint) and install it elevated via `msiexec /i YourMsi.msi /l*v install.log`.

Confirm:

```powershell
Get-Website | Where-Object Name -eq "<YourSiteName>"
Get-ChildItem IIS:\SslBindings
```

`IIS:\SslBindings` should show your certificate thumbprint bound to the site's port 443
binding. Uninstall via `msiexec /x` and confirm the binding is removed
(`Get-ChildItem IIS:\SslBindings` no longer lists it).

### 2.3 — Per-package feature picker (`ADDLOCAL`)

Demo: `demo/16-features` (nested feature tree, `MsiDialogSet.FeatureTree`).

```powershell
dotnet build demo/16-features
dotnet run --project demo/16-features -- -o out
```

Static verification (no interactive UI needed) — install with an explicit `ADDLOCAL` that
excludes the optional features:

```powershell
msiexec /i "out\Demo 16 Features-1.0.0.msi" ADDLOCAL="Application" /qn /l*v install.log
```

Confirm only the required `Application` feature's files landed:

```powershell
Get-ChildItem "$env:ProgramFiles\Demo\FeatureDemo" -Recurse
```

`Plugins\editor.dll` and `Docs\readme.txt` must be **absent** — if they're present, `ADDLOCAL`
did not filter correctly. Then reinstall with `ADDLOCAL="Application,Plugins,Documentation"`
and confirm all three now exist. Uninstall to clean up:
`msiexec /x "out\Demo 16 Features-1.0.0.msi" /qn`.

(The interactive picker itself — the built-in UI rendering the feature tree and sending back a
selection over the named pipe — is exercised by using the turnkey UI in Part 2.5 against a
bundle that wraps this MSI and choosing features by hand in the wizard, instead of passing
`ADDLOCAL` on the command line.)

### 2.4 — External/downloadable bundle container

Demo: `demo/10-advanced-bundle`. Its `Program.cs` defines two containers with placeholder URLs
(`https://cdn.northwind.example.com/...`) — you must point them at a real local host before this
proves anything.

1. Edit `demo/10-advanced-bundle/bundle/Program.cs`, changing both `.DownloadUrl(...)` calls to
   point at a local static file server you control, e.g. `http://localhost:8080/prereqs/` and
   `http://localhost:8080/app/`.
2. Build the demo's dependencies (`msi-package`, the `prereq.exe`/`hotfix.msu`/`patch.msp`
   payload fixtures it references) per `demo/10-advanced-bundle/README.md`, then:
   ```powershell
   dotnet run --project demo/10-advanced-bundle/bundle -- -o out
   ```
   The compiler writes the external-container payloads as separate `.ffcontainer` files
   alongside the bundle exe (BDL035 fires only if a container is declared but never assigned a
   payload — a clean build with no BDL035 confirms the container has a payload).
3. Serve the `out` directory's `.ffcontainer` files at the URLs from step 1, e.g.:
   ```powershell
   dotnet tool install -g dotnet-serve
   dotnet-serve --directory out --port 8080
   ```
4. Run the bundle exe elevated in a **separate** shell. Watch (Process Monitor, or just the
   `dotnet-serve` request log) for `ExternalContainerAcquirer` fetching each `.ffcontainer` over
   HTTP before its package installs.
5. Confirm the install succeeds and the packages that were behind the external containers are
   actually present (`Get-Package`). To prove the fail-loud path, stop the local server or
   corrupt one served `.ffcontainer` file and rerun — the install must fail closed, not install
   an unverified payload.

### 2.5 — Turnkey built-in UI host

Demo: `demo/35-bundle-simple` (`UseBuiltInUI(themeColor: "#0078D4")`) — the simplest bundle that
uses the built-in UI, no custom UI project involved.

```powershell
dotnet run --project demo/35-bundle-simple -- -o out
```

Run `out\Simple Bundle-1.0.0.exe` elevated (double-click, or from a shell). Confirm:

- A window actually appears (this is the literal bug the turnkey host fixed — before it, the
  WPF entry point never read `--manifest`/`--pipe`/`--secret-pipe` and no window showed at all).
- Check Task Manager / `Get-Process` for `FalkForge.Ui` while the wizard is open — the engine
  (`FalkForge.Engine.exe`) extracts and spawns it as a separate process.
- Click through to install; confirm `Get-Package "My Application"` afterward, then uninstall via
  Programs & Features.

### 2.6 — Per-culture install UI

Demo: `demo/08-localization` (culture fallback chain, `AddBuiltInCultures()` +
`AddJsonFile()` per culture, `!(loc.StringId)` references).

```powershell
dotnet build demo/08-localization
dotnet run --project demo/08-localization -- -o out
```

This demo builds an MSI (not a built-in-UI bundle), so its localized strings render through the
native MSI UI, driven by the OS's installer locale. To see a non-English culture, run:

```powershell
msiexec /i "out\<demo-08-output>.msi" TRANSFORMS=:1031   # 1031 = German LCID; adjust as needed
```

or change your Windows display language / install with a matching Windows locale, then confirm
the wizard shows the localized strings (e.g. the German `strings.de.json` values) instead of the
`en-US` defaults, and that the `de-AT` partial override (`FinishMessage`) falls back correctly
through `de` → not `en-US` for the one overridden key. Also spot-check `forge loc export --list`
to confirm the built-in culture catalog CLI path works.

### 2.7 — Authenticode detach/sign/reattach ceremony

Demo: `demo/15-bundle-signing`. Its own README says the signing step is a **placeholder** — do
the real thing by hand:

```powershell
dotnet build demo/15-bundle-signing/msi-package/msi-package.csproj
dotnet run --project demo/15-bundle-signing/bundle -- -o out
```

That produces an unsigned bundle exe. Detach, sign with `signtool` using the code-signing cert
from Part 0 step 4, then reattach:

```powershell
forge bundle detach "out\Bundle Signing Demo-1.0.0.exe" --stub stub.exe --data bundle.dat
signtool sign /sha1 $codeSignCert.Thumbprint /fd SHA256 /t http://timestamp.digicert.com stub.exe
forge bundle reattach --stub stub.exe --data bundle.dat -o signed.exe
```

Confirm the reattached bundle still carries a valid signature AND still installs correctly:

```powershell
Get-AuthenticodeSignature signed.exe | Format-List *
```

With a self-signed, non-trusted-root cert, expect `Status: UnknownError` /
`StatusMessage` referencing `CERT_E_UNTRUSTEDROOT` — that's the cryptographically-correct
result for an untrusted root (the digest verifies; only chain trust is missing). Import
`$codeSignCert` into `Cert:\LocalMachine\Root` on the VM if you want to see a full `Valid`
status instead (do this only on the disposable VM). Then run `signed.exe` elevated and confirm
it installs normally — the reattach must not have corrupted the self-extraction, matching what
`BundleDetachSignRoundTripTests.DetachReattach_SelfExtractsOriginalPayloadBytes` already proves
in-process, but now through the real `signtool` binary instead of an in-test fake signature.

## Cleanup

After each section, remove what you installed (Programs & Features, `Remove-Website`,
`Remove-WebAppPool`, drop any SQL databases created, delete local users/groups/shares created by
Part 1). Since this all ran on a disposable VM, the simplest reliable cleanup is discarding the
VM snapshot.
