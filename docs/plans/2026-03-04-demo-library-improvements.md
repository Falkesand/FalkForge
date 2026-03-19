# Demo Library Improvements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix broken demos, expand existing demos to showcase all builder API methods, add factory method to DotNetExtension, and update READMEs so the demo library becomes the primary API discoverability tool.

**Architecture:** One small API change (DotNetExtension factory method with TDD), then expand ~20 existing demos with missing builder methods, then update READMEs. Each demo becomes the complete reference for its feature area.

**Tech Stack:** C# / .NET, FalkForge fluent API, xUnit for tests

---

## Task 1: DotNet Extension Factory Method

**Files:**
- Modify: `src/FalkForge.Extensions.DotNet/DotNetExtension.cs`
- Modify: `tests/FalkForge.Extensions.DotNet.Tests/` (find or create test file)

**Step 1: Write the failing test**

Find the test project and add a test:

```csharp
[Fact]
public void SearchForRuntime_ReturnsBuilder_ThatBuildsSuccessfully()
{
    var ext = new DotNetExtension();
    var result = ext.SearchForRuntime()
        .RuntimeType(DotNetRuntimeType.Runtime)
        .Platform(DotNetPlatform.X64)
        .MinVersion(new Version(8, 0, 0))
        .Variable("DOTNET8_FOUND")
        .Build();

    Assert.True(result.IsSuccess);
    Assert.Equal(DotNetRuntimeType.Runtime, result.Value.RuntimeType);
    Assert.Equal("DOTNET8_FOUND", result.Value.VariableName);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test D:\Git\FalkInstaller\tests\FalkForge.Extensions.DotNet.Tests --filter SearchForRuntime`
Expected: FAIL — `DotNetExtension` has no `SearchForRuntime` method

**Step 3: Implement the factory method**

In `src/FalkForge.Extensions.DotNet/DotNetExtension.cs`, add:

```csharp
/// <summary>
/// Creates a new search builder for detecting .NET Core/5+ runtimes.
/// </summary>
public DotNetCoreSearchBuilder SearchForRuntime()
{
    return new DotNetCoreSearchBuilder();
}
```

Full file becomes:

```csharp
using FalkForge.Extensibility;

namespace FalkForge.Extensions.DotNet;

public sealed class DotNetExtension : IFalkForgeExtension
{
    public string Name => "DotNet";

    public void Register(IExtensionRegistry registry)
    {
        // DotNet extension provides detection capabilities only.
        // It does not contribute MSI tables or components.
        // Detection results are populated as variables by the engine
        // via DotNetDetector during the detect phase.
    }

    /// <summary>
    /// Creates a new search builder for detecting .NET Core/5+ runtimes.
    /// </summary>
    public DotNetCoreSearchBuilder SearchForRuntime()
    {
        return new DotNetCoreSearchBuilder();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test D:\Git\FalkInstaller\tests\FalkForge.Extensions.DotNet.Tests --filter SearchForRuntime`
Expected: PASS

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.DotNet/DotNetExtension.cs tests/FalkForge.Extensions.DotNet.Tests/
git commit -m "feat(DotNet): add SearchForRuntime factory method to DotNetExtension"
```

---

## Task 2: Fix Demo 32 — ext-dotnet (use factory + wire launch condition)

**Files:**
- Modify: `demo/32-ext-dotnet/Program.cs`

**Step 1: Rewrite Program.cs to use the factory and wire the search result as a launch condition**

```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Extensions.DotNet;

// Detect whether .NET 8.0+ runtime is installed and block install if missing.
var dotnet = new DotNetExtension();

var search = dotnet.SearchForRuntime()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .Platform(DotNetPlatform.X64)
    .MinVersion(new Version(8, 0, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

if (search.IsFailure)
{
    Console.Error.WriteLine(search.Error);
    return 1;
}

Console.WriteLine($".NET Detection: search for {search.Value.RuntimeType} >= {search.Value.MinimumVersion}");

return Installer.Build(args, package =>
{
    package.Name = ".NET Detection Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    // Use the search variable as a launch condition — block install if .NET 8 is missing
    package.Require(r => r
        .Condition("DOTNET8_FOUND")
        .Message(".NET 8.0 Runtime (x64) or later is required. Please install it from https://dotnet.microsoft.com/download"));

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "DotNetDemo"));

}, new MsiCompiler());
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\32-ext-dotnet\32-ext-dotnet.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/32-ext-dotnet/Program.cs
git commit -m "fix(demo): wire DotNet search result as launch condition in demo 32"
```

---

## Task 3: Expand Demo 17 — services (failure actions, credentials, service control)

**Files:**
- Modify: `demo/17-services/Program.cs`

**Step 1: Expand Program.cs with failure actions, DependsOnGroup, UserName/Password, and ServiceControl options**

```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Install a Windows service with startup dependencies, failure recovery, and service control.
return Installer.Build(args, package =>
{
    package.Name = "Service Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/myservice.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "ServiceDemo"));

    // Install and configure a Windows service
    package.Service("DemoService", svc =>
    {
        svc.DisplayName = "Demo Background Service";
        svc.Description = "Demonstrates FalkForge service installation";
        svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
        svc.StartMode = ServiceStartMode.Automatic;
        svc.Account = ServiceAccount.LocalService;

        // Depend on a specific service (won't start until Tcpip is running)
        svc.DependsOn("Tcpip");

        // Depend on a service group (waits for all services in the network group)
        svc.DependsOnGroup("NetworkProvider");

        // Configure failure recovery actions
        svc.FailureActions(fa =>
        {
            fa.OnFirstFailure = FailureAction.Restart;
            fa.OnSecondFailure = FailureAction.Restart;
            fa.OnSubsequentFailures = FailureAction.None;
            fa.ResetPeriod = TimeSpan.FromDays(1);
            fa.RestartDelay = TimeSpan.FromSeconds(30);
        });
    });

    // A second service running under a domain account
    package.Service("DemoWorker", svc =>
    {
        svc.DisplayName = "Demo Worker Service";
        svc.Executable = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe";
        svc.StartMode = ServiceStartMode.Manual;
        svc.UserName = @".\DemoUser";
        svc.Password = "[DEMO_PASSWORD]";

        // Run a diagnostic command on failure
        svc.FailureActions(fa =>
        {
            fa.OnFirstFailure = FailureAction.RunCommand;
            fa.Command = @"[ProgramFilesFolder]Demo\ServiceDemo\myservice.exe --diagnose";
            fa.OnSecondFailure = FailureAction.Restart;
            fa.OnSubsequentFailures = FailureAction.Reboot;
            fa.RebootMessage = "Demo Worker service has failed repeatedly. Rebooting.";
        });
    });

    // Service control — stop before uninstall, start after install
    package.ServiceControl(sc =>
    {
        sc.ServiceName("DemoService");
        sc.StopOnUninstall();
        sc.StartOnInstall();
        sc.DeleteOnUninstall();
        sc.Wait(true);
    });

    // Control an existing service — stop during install, pass arguments on start
    package.ServiceControl(sc =>
    {
        sc.ServiceName("DemoWorker");
        sc.StopOnInstall();
        sc.StartOnInstall();
        sc.Arguments("--config=[INSTALLDIR]config.json");
        sc.DeleteOnUninstall();
    });

}, new MsiCompiler());
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\17-services\17-services.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/17-services/Program.cs
git commit -m "feat(demo): expand demo 17 with failure actions, credentials, DependsOnGroup, ServiceControl options"
```

---

## Task 4: Expand Demo 20 — custom actions (DllFromBinary, ExeFromBinary, Commit, ContinueOnError)

**Files:**
- Modify: `demo/20-custom-actions/Program.cs`

**Step 1: Expand Program.cs to show all custom action types**

```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Custom actions: SetProperty, DllFromBinary, ExeFromBinary, deferred, rollback, commit.
return Installer.Build(args, package =>
{
    package.Name = "Custom Actions Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "CustomActionDemo"));

    // Embed a DLL binary for use in DLL-based custom actions
    package.Binary("CustomActionsDll", "payload/CustomActions.dll");

    // --- Type 1: DLL-based custom action (embedded DLL with C entry point) ---
    package.CustomAction("CheckSystemRequirements", ca =>
    {
        ca.DllFromBinary("CustomActionsDll", "CheckRequirements");
        ca.After = "CostFinalize";
        ca.Condition = Condition.IsInstalling.ToString();
    });

    // --- Type 2: EXE-based custom action (embedded EXE) ---
    package.CustomAction("RunSetupTool", ca =>
    {
        ca.ExeFromBinary("CustomActionsDll");
        ca.Target = "--setup --silent";
        ca.Deferred();
        ca.NoImpersonate();
        ca.After = "InstallFiles";
    });

    // --- Type 51: SetProperty custom action ---
    package.CustomAction("SetInstallMode", ca =>
    {
        ca.SetProperty("INSTALL_MODE", "standard");
        ca.Condition = Condition.IsInstalling.ToString();
    });

    // --- Deferred + rollback pair ---
    package.CustomAction("ConfigureApp", ca =>
    {
        ca.SetProperty("CONFIGURE_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --configure");
        ca.Deferred();
        ca.NoImpersonate();
        ca.After = "InstallFiles";
    });

    package.CustomAction("UndoConfigureApp", ca =>
    {
        ca.SetProperty("UNDO_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --unconfigure");
        ca.Rollback();
        ca.NoImpersonate();
        ca.Before = "ConfigureApp";
    });

    // --- Commit action (runs only on successful install completion) ---
    package.CustomAction("NotifySuccess", ca =>
    {
        ca.SetProperty("NOTIFY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --notify-complete");
        ca.Commit();
        ca.NoImpersonate();
        ca.After = "ConfigureApp";
    });

    // --- ContinueOnError (installer proceeds even if this CA fails) ---
    package.CustomAction("OptionalTelemetry", ca =>
    {
        ca.SetProperty("TELEMETRY_CMD", @"[ProgramFilesFolder]Demo\CustomActionDemo\app.exe --telemetry");
        ca.Deferred();
        ca.ContinueOnError();
        ca.After = "NotifySuccess";
    });

}, new MsiCompiler());
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\20-custom-actions\20-custom-actions.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/20-custom-actions/Program.cs
git commit -m "feat(demo): expand demo 20 with DllFromBinary, ExeFromBinary, Commit, ContinueOnError"
```

---

## Task 5: Expand Demo 02 — notepad clone (registry DWord, RemoveRegistry, shortcut options)

**Files:**
- Modify: `demo/02-notepad-clone/Program.cs`

**Step 1: Expand Program.cs with DWord, DefaultValue, RemoveRegistry, and shortcut options**

```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// A small application installer with shortcuts, registry, major upgrade, and license.
return Installer.Build(args, package =>
{
    package.Name = "FalkPad";
    package.Manufacturer = "Falk Software";
    package.Version = new Version(2, 1, 0);
    package.Scope = InstallScope.PerMachine;
    package.Architecture = ProcessorArchitecture.X64;
    package.Description = "A simple text editor";
    package.LicenseFile = "payload/license.rtf";

    package.UseDialogSet(MsiDialogSet.InstallDir);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    // Application files
    package.Files(files => files
        .Add("payload/falkpad.exe")
        .Add("payload/falkpad.dll")
        .Add("payload/readme.txt")
        .Add("payload/license.rtf")
        .To(KnownFolder.ProgramFiles / "Falk Software" / "FalkPad"));

    // Desktop shortcut with icon
    package.Shortcut("FalkPad", "falkpad.exe")
        .WithIcon("payload/falkpad.ico")
        .WithDescription("Launch FalkPad text editor")
        .OnDesktop();

    // Start menu shortcut under company subfolder
    package.Shortcut("FalkPad", "falkpad.exe")
        .WithIcon("payload/falkpad.ico")
        .WithDescription("Launch FalkPad text editor")
        .OnStartMenu("Falk Software");

    // Startup shortcut — launches on Windows login
    package.Shortcut("FalkPad Startup", "falkpad.exe")
        .WithArguments("--minimized")
        .WithWorkingDirectory(@"[ProgramFilesFolder]Falk Software\FalkPad")
        .OnStartup();

    // Registry entries — string values, DWORD, and default value
    package.Registry(reg => reg
        .Key(RegistryRoot.LocalMachine, @"Software\FalkSoftware\FalkPad", key =>
        {
            key.Value("Version", "2.1.0");
            key.Value("InstallPath", MsiProperty.InstallDir);
            key.DWord("EditorFlags", 3);
            key.DefaultValue("FalkPad Text Editor");
        }));

    // Remove registry entries on uninstall
    package.RemoveRegistry(rr => rr
        .Root(RegistryRoot.LocalMachine)
        .Key(@"Software\FalkSoftware\FalkPad")
        .RemoveKey());

    // Major upgrade support — block downgrades
    package.MajorUpgrade(_ => { });
    package.Downgrade(d => d.Block("A newer version of FalkPad is already installed. Please uninstall it first."));

}, new MsiCompiler());
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\02-notepad-clone\02-notepad-clone.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/02-notepad-clone/Program.cs
git commit -m "feat(demo): expand demo 02 with DWord, DefaultValue, RemoveRegistry, shortcut options"
```

---

## Task 6: Expand Demo 40 — bundle variables (Secret, Hidden, Persisted)

**Files:**
- Modify: `demo/40-bundle-variables/Program.cs`

**Step 1: Add Secret, Hidden, and Persisted variable examples**

```csharp
using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Define variables with visibility controls and conditional package installation.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Variables Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("E1E2E3E4-F5F6-4A7A-8B8B-9C9C0D0D1E1E"))
        .UpgradeCode(new Guid("F1F2F3F4-A5A6-4B7B-8C8C-9D9D0E0E1F1F"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        // Standard numeric variable with default
        .Variable("InstallOptionalTools", v => v
            .Numeric()
            .Default("0"))
        // Persisted variable — survives bundle repair/modify sessions
        .Variable("InstallPath", v => v
            .String()
            .Default(@"C:\Program Files\Demo")
            .Persisted())
        // Hidden variable — excluded from install logs
        .Variable("LicenseKey", v => v
            .String()
            .Hidden())
        // Secret variable — excluded from logs AND persisted state (implies Hidden)
        .Variable("DatabasePassword", v => v
            .String()
            .Secret())
        .Chain(chain => chain
            .MsiPackage("CoreApp.msi", p => p
                .Id("CoreApp")
                .DisplayName("Core Application")
                .Vital(true))
            .MsiPackage("OptionalTools.msi", p => p
                .Id("OptionalTools")
                .DisplayName("Optional Developer Tools")
                .Vital(false)
                .InstallCondition("InstallOptionalTools = 1")))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\40-bundle-variables\40-bundle-variables.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/40-bundle-variables/Program.cs
git commit -m "feat(demo): expand demo 40 with Secret, Hidden, Persisted bundle variables"
```

---

## Task 7: Expand Demo 35 — bundle simple (RelatedBundle, DependencyProvider)

**Files:**
- Modify: `demo/35-bundle-simple/Program.cs`

**Step 1: Add RelatedBundle and dependency management**

```csharp
using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// A bundle with related bundle detection and dependency management.
return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("Simple Bundle")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D"))
        .UpgradeCode(new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI(themeColor: "#0078D4")
        // Detect a related bundle (e.g. a previous version using a different upgrade code)
        .RelatedBundle("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F")
        // Declare this bundle as a dependency provider (other bundles can depend on it)
        .DependencyProvider("Demo.SimpleBundle", "1.0.0", "Simple Bundle")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    return new BundleCompiler().Compile(bundle, outputPath);
});
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\35-bundle-simple\35-bundle-simple.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/35-bundle-simple/Program.cs
git commit -m "feat(demo): expand demo 35 with RelatedBundle and DependencyProvider"
```

---

## Task 8: Expand Demo 16 — features (MajorUpgrade tuning)

**Files:**
- Modify: `demo/16-features/Program.cs`

**Step 1: Add MajorUpgrade with AllowSameVersionUpgrades, Schedule, MigrateFeatures**

Add before the closing `}, new MsiCompiler());`:

```csharp
    // Major upgrade — migrate user's feature selections from the old version
    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
        mu.MigrateFeatures(true);
    });
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\16-features\16-features.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/16-features/Program.cs
git commit -m "feat(demo): expand demo 16 with MajorUpgrade tuning (AllowSameVersion, Schedule, MigrateFeatures)"
```

---

## Task 9: Expand Demo 01 — hello world (MediaTemplate, Reproducible, RestartManager)

**Files:**
- Modify: `demo/01-hello-world/Program.cs`

**Step 1: Add MediaTemplate, Reproducible, and RestartManager**

```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

// The simplest possible installer: one file, no features, Minimal dialog set.
return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    // Cabinet file settings — naming template, compression level, embedding
    package.MediaTemplate(mt =>
    {
        mt.CabinetTemplate("data{0}.cab");
        mt.CompressionLevel(FalkForge.CompressionLevel.High);
        mt.EmbedCabinet(true);
    });

    // Enable deterministic builds (same source → identical MSI output)
    package.Reproducible();

    // Enable Windows Restart Manager — gracefully close files-in-use during install
    package.EnableRestartManagerSupport();

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));

}, new MsiCompiler());
```

**Step 2: Verify it compiles**

Run: `dotnet build D:\Git\FalkInstaller\demo\01-hello-world\01-hello-world.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add demo/01-hello-world/Program.cs
git commit -m "feat(demo): expand demo 01 with MediaTemplate, Reproducible, RestartManager"
```

---

## Task 10: Expand remaining medium-impact demos

For each of these, read the current Program.cs, add the missing features, verify compilation, and commit.

### 10a: Demo 23 — permissions (Sddl, ForTable)

**Modify:** `demo/23-permissions/Program.cs`

Add a second permission using SDDL syntax and ForTable:

```csharp
    // Permission via SDDL string — fine-grained access control
    package.Permission("DataFolder", p =>
    {
        p.Sddl = "D:(A;;FA;;;BA)(A;;FR;;;BU)";
        p.ForTable("CreateFolder");
    });
```

### 10b: Demo 15 — bundle signing (advanced options)

**Modify:** `demo/15-bundle-signing/msi-package/Program.cs`

Add to the Signing call:

```csharp
    package.Signing(s =>
    {
        s.Thumbprint("ABC123...");
        s.Store("My");
        s.Timestamp("http://timestamp.digicert.com");
        s.Algorithm("sha256");
        s.WithDescription("FalkForge Demo Installer", "https://example.com");
    });
```

### 10c: Demo 44 — merge module (Dependency)

**Modify:** `demo/44-merge-module/Program.cs`

Add: `.Dependency("SharedRuntime_1.0")`

### 10d: Demo 45 — patch (Classification, AllowRemoval, TargetProduct, TargetVersion)

**Modify:** `demo/45-patch/Program.cs`

Add:

```csharp
    .Classification(PatchClassification.Hotfix)
    .AllowRemoval(true)
    .TargetProduct(new Guid("..."))
    .TargetVersion("1.0.0")
    .UpdatedVersion("1.0.1")
```

**After all sub-tasks, verify and commit:**

Run: `dotnet build D:\Git\FalkInstaller\demo\FalkForge.Demos.slnx`
Expected: Build succeeded, 0 errors

```bash
git add demo/23-permissions/ demo/15-bundle-signing/ demo/44-merge-module/ demo/45-patch/
git commit -m "feat(demo): expand demos 23, 15, 44, 45 with advanced builder options"
```

---

## Task 11: Expand remaining low-impact demos

### 11a: Demo 18 — environment variables (user-scoped)

**Modify:** `demo/18-environment-variables/Program.cs`

Add a user-scoped environment variable:

```csharp
    // User-scoped variable (not system-wide)
    package.EnvironmentVariable("DEMO_USER_PREF", "enabled", ev =>
    {
        ev.IsSystem = false;
        ev.Action = EnvironmentVariableAction.Set;
    });
```

### 11b: Demo 24 — fonts (Title override)

**Modify:** `demo/24-fonts/Program.cs`

Add custom title:

```csharp
    package.Font("payload/DemoSans.ttf", f =>
    {
        f.Title = "Demo Sans Regular";
    });
```

### 11c: Demo 25 — file operations (ComponentCondition)

**Modify:** `demo/25-file-operations/Program.cs`

Add conditional file installation:

```csharp
    package.Files(files => files
        .Add("payload/debug-tools.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "FileOpsDemo" / "Debug")
        .ComponentCondition("INSTALL_DEBUG_TOOLS"));
```

### 11d: Demo 28 — sequence scheduling (UISequence)

**Modify:** `demo/28-sequence-scheduling/Program.cs`

Add UISequence scheduling example:

```csharp
    // Schedule an action in the UI sequence (runs during user interaction phase)
    package.UISequence(seq =>
    {
        // same SequenceBuilder API as ExecuteSequence
    });
```

### 11e: Demo 14 — lifecycle hooks (secure properties)

**Modify:** `demo/14-lifecycle-hooks/Program.cs` — this is a WPF app entry point, so add secure properties to the associated MSI project if one exists, or add a comment showing the pattern. Check if there is a separate MSI project for demo 14.

**After all sub-tasks, verify and commit:**

Run: `dotnet build D:\Git\FalkInstaller\demo\FalkForge.Demos.slnx`
Expected: Build succeeded, 0 errors

```bash
git add demo/18-environment-variables/ demo/24-fonts/ demo/25-file-operations/ demo/28-sequence-scheduling/ demo/14-lifecycle-hooks/
git commit -m "feat(demo): expand demos 18, 24, 25, 28, 14 with remaining builder options"
```

---

## Task 12: Update READMEs for all expanded demos

For each demo modified in Tasks 2-11, update its README.md:
- Add new bullet points in "What This Demonstrates"
- Add new code snippets or table rows in "Key API Calls"
- Add new entries in "Notes" for caveats

Demos to update: 01, 02, 14, 15, 16, 17, 18, 20, 23, 24, 25, 28, 32, 35, 40, 44, 45

**After all READMEs updated:**

```bash
git add demo/*/README.md demo/*/*/README.md
git commit -m "docs(demo): update READMEs to document expanded builder API coverage"
```

---

## Task 13: Update master demo/README.md feature matrix

**Files:**
- Modify: `demo/README.md`

Update the feature matrix table to reflect that demos now cover all builder methods. Add a "Builder Coverage" column or section listing which builder class each demo exercises.

```bash
git add demo/README.md
git commit -m "docs(demo): update master README feature matrix with full API coverage"
```

---

## Task 14: Final verification

**Step 1: Build entire demo solution**

Run: `dotnet build D:\Git\FalkInstaller\demo\FalkForge.Demos.slnx`
Expected: Build succeeded, 0 errors, 0 warnings

**Step 2: Build main solution (ensure API change doesn't break anything)**

Run: `dotnet build D:\Git\FalkInstaller\FalkForge.slnx`
Expected: Build succeeded

**Step 3: Run DotNet extension tests**

Run: `dotnet test D:\Git\FalkInstaller\tests\FalkForge.Extensions.DotNet.Tests`
Expected: All tests pass
