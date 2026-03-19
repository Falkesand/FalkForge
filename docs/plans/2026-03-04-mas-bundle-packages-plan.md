# MAS Bundle with MSI Packages — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create 5 MSI package projects for MAS, a bundle project that chains them, complete the engine runtime bootstrapper, and add progress/completion pages so MAS can actually install packages.

**Architecture:** The bundle exe is a NativeAOT-compiled FalkForge.Engine with embedded payloads (5 MSIs + manifest). At runtime it extracts packages, launches the UI (MAS.exe) via pipe, detects installed state, then silently installs each MSI when the user clicks Install. The UI shows real-time progress.

**Tech Stack:** FalkForge.Core (Installer.Build, MsiCompiler), FalkForge.Compiler.Bundle (BundleBuilder, BundleCompiler), FalkForge.Engine (EngineHost, PackageExecutor), FalkForge.Ui (InstallerApp, EngineClient), WPF/XAML, NativeAOT

---

## Task 1: Create Dummy Stub Executable

**Files:**
- Create: `demo/MAS/packages/stub/dummy.exe`

**Step 1: Create a minimal .NET console app that serves as our dummy**

Create a temporary project to build the stub:

```bash
mkdir -p D:/Git/FalkInstaller/demo/MAS/packages/stub
```

Create `demo/MAS/packages/stub/Stub.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>dummy</AssemblyName>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
</Project>
```

Create `demo/MAS/packages/stub/Program.cs`:
```csharp
Console.WriteLine("MultiAccess Suite — placeholder component");
```

**Step 2: Build and keep the output**

```bash
dotnet publish D:/Git/FalkInstaller/demo/MAS/packages/stub/Stub.csproj -c Release -o D:/Git/FalkInstaller/demo/MAS/packages/stub/out
```

The `dummy.exe` at `demo/MAS/packages/stub/out/dummy.exe` is our payload. Each MSI will copy it.

**Step 3: Commit**

```
feat(demo/MAS): add dummy stub executable for MSI payloads
```

---

## Task 2: Create 5 MSI Package Projects

**Files:**
- Create: `demo/MAS/packages/MultiAccess/Program.cs`
- Create: `demo/MAS/packages/MultiAccess/MultiAccess.csproj`
- Create: `demo/MAS/packages/MultiServer/Program.cs`
- Create: `demo/MAS/packages/MultiServer/MultiServer.csproj`
- Create: `demo/MAS/packages/MultiServerEx/Program.cs`
- Create: `demo/MAS/packages/MultiServerEx/MultiServerEx.csproj`
- Create: `demo/MAS/packages/Konfigurera/Program.cs`
- Create: `demo/MAS/packages/Konfigurera/Konfigurera.csproj`
- Create: `demo/MAS/packages/Concatenate/Program.cs`
- Create: `demo/MAS/packages/Concatenate/Concatenate.csproj`

**Step 1: Create each MSI project**

Each project follows the same pattern. Example for MultiAccess:

`demo/MAS/packages/MultiAccess/MultiAccess.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>MAS.Packages.MultiAccess</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../src/FalkForge.Core/FalkForge.Core.csproj" />
    <ProjectReference Include="../../../../src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="../stub/out/dummy.exe" CopyToOutputDirectory="PreserveNewest" Link="payload/MultiAccess.exe" />
  </ItemGroup>
</Project>
```

`demo/MAS/packages/MultiAccess/Program.cs`:
```csharp
using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "MultiAccess";
    package.Manufacturer = "ASSA ABLOY";
    package.Version = new Version(8, 9, 0);
    package.UpgradeCode = new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D");
    package.Scope = InstallScope.PerMachine;
    package.UseDialogSet(MsiDialogSet.None);
    package.Reproducible();

    package.Files(files => files
        .Add("payload/MultiAccess.exe")
        .To(KnownFolder.ProgramFiles / "ASSA ABLOY" / "MultiAccess"));

    package.MajorUpgrade(mu =>
    {
        mu.AllowSameVersionUpgrades();
        mu.Schedule(RemoveExistingProductsSchedule.AfterInstallExecute);
    });
}, new MsiCompiler());
```

Repeat for all 5 packages with unique UpgradeCode GUIDs:
- **MultiAccess**: `A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D`
- **MultiServer**: `B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E`
- **MultiServerEx**: `C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F`
- **Konfigurera**: `D4E5F6A7-B8C9-4D0E-1F2A-3B4C5D6E7F80`
- **Concatenate**: `E5F6A7B8-C9D0-4E1F-2A3B-4C5D6E7F8091`

Each uses `MsiDialogSet.None` (silent — no MSI UI).

**Step 2: Add all projects to the solution**

```bash
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/packages/MultiAccess/MultiAccess.csproj
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/packages/MultiServer/MultiServer.csproj
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/packages/MultiServerEx/MultiServerEx.csproj
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/packages/Konfigurera/Konfigurera.csproj
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/packages/Concatenate/Concatenate.csproj
```

**Step 3: Build and verify all 5 produce .msi files**

```bash
dotnet run --project D:/Git/FalkInstaller/demo/MAS/packages/MultiAccess/MultiAccess.csproj -- -o D:/Git/FalkInstaller/demo/MAS/packages/MultiAccess/bin/Release
```

Expected: `MultiAccess-8.9.0.msi` produced in output directory. Repeat for all 5.

**Step 4: Commit**

```
feat(demo/MAS): add 5 MSI package projects with dummy payloads
```

---

## Task 3: Create Bundle Project

**Files:**
- Create: `demo/MAS/bundle/MASBundle.csproj`
- Create: `demo/MAS/bundle/Program.cs`

**Step 1: Create the bundle project**

`demo/MAS/bundle/MASBundle.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>MAS.Bundle</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../src/FalkForge.Core/FalkForge.Core.csproj" />
    <ProjectReference Include="../../../src/FalkForge.Compiler.Bundle/FalkForge.Compiler.Bundle.csproj" />
  </ItemGroup>
</Project>
```

`demo/MAS/bundle/Program.cs`:
```csharp
using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Models;

// MSI paths are resolved relative to this project's output directory.
// In practice, the MSIs are built first and placed at known paths.
var packagesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

string MsiPath(string name) => Path.Combine(packagesDir, name, "bin", "Release", $"{name}-8.9.0.msi");

return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("MultiAccess Suite")
        .Manufacturer("ASSA ABLOY")
        .Version("8.9.0")
        .BundleId(new Guid("10203040-5060-4070-8090-A0B0C0D0E0F0"))
        .UpgradeCode(new Guid("F0E0D0C0-B0A0-4090-8070-605040302010"))
        .Scope(InstallScope.PerMachine)
        .UseCustomUI("../../MAS.csproj")
        .Chain(chain => chain
            .MsiPackage(MsiPath("MultiAccess"), p => p
                .Id("MultiAccess")
                .DisplayName("MultiAccess")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("MultiServer"), p => p
                .Id("MultiServer")
                .DisplayName("MultiServer")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("MultiServerEx"), p => p
                .Id("MultiServerEx")
                .DisplayName("MultiServerEx")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("Konfigurera"), p => p
                .Id("Konfigurera")
                .DisplayName("Konfigurera")
                .Version("8.9.0")
                .Vital(true))
            .MsiPackage(MsiPath("Concatenate"), p => p
                .Id("Concatenate")
                .DisplayName("Concatenate")
                .Version("8.9.0")
                .Vital(true)))
        .Build();

    var compiler = new BundleCompiler();
    return compiler.Compile(bundle, outputPath);
});
```

**Step 2: Add to solution and build**

```bash
dotnet sln D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx add D:/Git/FalkInstaller/demo/MAS/bundle/MASBundle.csproj
```

Build: First build the 5 MSI projects, then run the bundle project.

**Step 3: Commit**

```
feat(demo/MAS): add bundle project chaining 5 MSI packages
```

---

## Task 4: Engine — Load Manifest from File

**Files:**
- Modify: `src/FalkForge.Engine/Program.cs`

**Step 1: Write failing test**

Create test in `tests/FalkForge.Engine.Tests/` that verifies manifest loading from a JSON file (or verify the existing test coverage and add a focused test).

**Step 2: Implement manifest loading**

Replace the stub in `src/FalkForge.Engine/Program.cs`:

```csharp
namespace FalkForge.Engine;

using System.Text.Json;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol.Transport;
using FalkForge.Platform.Windows;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        string? manifestPath = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    pipeName = args[++i];
                    break;
                case "--secret":
                    _ = args[++i]; // consume and discard (deprecated)
                    break;
                case "--manifest":
                    manifestPath = args[++i];
                    break;
            }
        }

        if (manifestPath is null)
        {
            Console.Error.WriteLine("Usage: FalkForge.Engine --manifest <path> [--pipe <name>]");
            return 1;
        }

        // Load manifest from JSON file
        InstallerManifest manifest;
        try
        {
            var json = await File.ReadAllBytesAsync(manifestPath);
            manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest)
                       ?? throw new InvalidOperationException("Manifest deserialized to null");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load manifest: {ex.Message}");
            return 1;
        }

        // Create pipe options if pipe name provided
        PipeConnectionOptions? pipeOptions = null;
        if (pipeName is not null)
        {
            // Read shared secret from init pipe (same pattern as Engine.Elevation)
            var secretPipeName = $"{pipeName}_init";
            var secret = new byte[32];
            try
            {
                using var initPipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".", secretPipeName, System.IO.Pipes.PipeDirection.In);
                await initPipe.ConnectAsync(TimeSpan.FromSeconds(30));
                var totalRead = 0;
                while (totalRead < 32)
                    totalRead += await initPipe.ReadAsync(secret.AsMemory(totalRead));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to receive secret: {ex.Message}");
                return 1;
            }

            pipeOptions = new PipeConnectionOptions
            {
                PipeName = pipeName,
                SharedSecret = secret
            };
        }

        // Run engine
        var platform = new WindowsPlatformServices();
        await using var host = new EngineHost(manifest, platform, pipeOptions);
        return await host.RunAsync();
    }
}
```

**Step 3: Verify build**

```bash
dotnet build D:/Git/FalkInstaller/src/FalkForge.Engine/FalkForge.Engine.csproj
```

**Step 4: Run tests**

```bash
dotnet test D:/Git/FalkInstaller/tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj
```

**Step 5: Commit**

```
feat(engine): load manifest from JSON file and connect via pipe
```

---

## Task 5: UI — Wire ResolveEngine() to Connect to Engine

**Files:**
- Modify: `src/FalkForge.Ui/InstallerApp.cs`

**Step 1: Write failing test**

Add test to `tests/FalkForge.Ui.Tests/` that verifies `ResolveEngine()` returns an `EngineClient` when `--manifest` and `--pipe` args are provided (with a test manifest file).

**Step 2: Implement ResolveEngine()**

Replace the stub in `src/FalkForge.Ui/InstallerApp.cs`:

```csharp
private static IInstallerEngine? ResolveEngine(string[] args)
{
    string? pipeName = null;
    string? manifestPath = null;

    for (var i = 0; i < args.Length - 1; i++)
        switch (args[i].ToLowerInvariant())
        {
            case "--pipe":
                pipeName = args[i + 1];
                break;
            case "--manifest":
                manifestPath = args[i + 1];
                break;
        }

    if (manifestPath is null || pipeName is null)
        return null;

    // Load manifest
    InstallerManifest manifest;
    try
    {
        var json = File.ReadAllBytes(manifestPath);
        manifest = System.Text.Json.JsonSerializer.Deserialize(
            json, FalkForge.Engine.Layout.LayoutJsonContext.Default.InstallerManifest)
            ?? throw new InvalidOperationException("Manifest deserialized to null");
    }
    catch
    {
        return null; // Fall back to NullInstallerEngine
    }

    // Generate shared secret and deliver via init pipe
    var secret = new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(secret);

    var secretPipeName = $"{pipeName}_init";
    var initPipeServer = new System.IO.Pipes.NamedPipeServerStream(
        secretPipeName,
        System.IO.Pipes.PipeDirection.Out,
        1,
        System.IO.Pipes.PipeTransmissionMode.Byte,
        System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);

    // Launch engine process
    var enginePath = FindEnginePath();
    if (enginePath is null)
        return null;

    var engineArgs = $"--manifest \"{manifestPath}\" --pipe {pipeName}";
    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = enginePath,
        Arguments = engineArgs,
        UseShellExecute = false,
        CreateNoWindow = true
    });

    // Deliver secret over init pipe
    Task.Run(async () =>
    {
        try
        {
            await initPipeServer.WaitForConnectionAsync();
            await initPipeServer.WriteAsync(secret);
            await initPipeServer.FlushAsync();
        }
        finally
        {
            await initPipeServer.DisposeAsync();
        }
    });

    var pipeOptions = new PipeConnectionOptions
    {
        PipeName = pipeName,
        SharedSecret = secret
    };

    var client = new EngineClient(pipeOptions, manifest);
    return client;
}

private static string? FindEnginePath()
{
    // Look for engine exe relative to the UI exe
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDir, "FalkForge.Engine.exe"),
        Path.Combine(baseDir, "..", "FalkForge.Engine", "FalkForge.Engine.exe"),
    };
    return candidates.FirstOrDefault(File.Exists);
}
```

**Note:** The `LayoutJsonContext` is `internal` to `FalkForge.Engine`. The UI project needs its own JSON context for manifest deserialization, or the Engine.Layout project needs to expose it. Check if `FalkForge.Engine.Protocol` already has one, otherwise create a `ManifestJsonContext` in `FalkForge.Ui` or make `LayoutJsonContext` public.

Also update `RunCore()` to call `ConnectAsync()` on the engine client after creation:

```csharp
var engine = ResolveEngine(args) ?? new NullInstallerEngine();

// Connect to engine if it's a real client
if (engine is EngineClient client)
{
    var connectResult = await client.ConnectAsync();
    if (connectResult.IsFailure)
        engine = new NullInstallerEngine(); // Fallback to design-time
}
```

This requires changing `RunCore` to be `async` or handling the connection on the startup handler.

**Step 3: Verify build**

```bash
dotnet build D:/Git/FalkInstaller/src/FalkForge.Ui/FalkForge.Ui.csproj
```

**Step 4: Run tests**

```bash
dotnet test D:/Git/FalkInstaller/tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj
```

**Step 5: Commit**

```
feat(ui): wire ResolveEngine to create EngineClient and launch engine process
```

---

## Task 6: BundleCompiler — Use Pre-built Engine as Stub

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/BundleCompiler.cs`

**Step 1: Add engine stub path option**

The `BundleCompiler` needs to accept a pre-built engine binary instead of creating an empty stub. Add a property or constructor parameter:

```csharp
public string? EngineStubPath { get; set; }
```

Modify `CreateStub()`:
```csharp
private string CreateStub(string outputDir)
{
    Directory.CreateDirectory(outputDir);

    if (EngineStubPath is not null && File.Exists(EngineStubPath))
    {
        // Use pre-built engine binary as the stub
        var stubPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
        File.Copy(EngineStubPath, stubPath, overwrite: true);
        return stubPath;
    }

    // Fallback: empty placeholder (design-time / test)
    var fallbackPath = Path.Combine(outputDir, $"stub_{Guid.NewGuid():N}.tmp");
    File.WriteAllBytes(fallbackPath, []);
    return fallbackPath;
}
```

**Step 2: Update bundle Program.cs to provide engine path**

In `demo/MAS/bundle/Program.cs`, set the engine stub path:

```csharp
var compiler = new BundleCompiler();

// Use pre-published NativeAOT engine binary as the bootstrapper stub
var enginePath = Environment.GetEnvironmentVariable("FALKFORGE_ENGINE_PATH");
if (enginePath is not null)
    compiler.EngineStubPath = enginePath;

return compiler.Compile(bundle, outputPath);
```

**Step 3: Write test**

Add test verifying BundleCompiler uses the provided stub path when set.

**Step 4: Verify build + tests**

```bash
dotnet build D:/Git/FalkInstaller/src/FalkForge.Compiler.Bundle/FalkForge.Compiler.Bundle.csproj
dotnet test D:/Git/FalkInstaller/tests/FalkForge.Compiler.Bundle.Tests/FalkForge.Compiler.Bundle.Tests.csproj
```

**Step 5: Commit**

```
feat(compiler): allow pre-built engine binary as bundle stub
```

---

## Task 7: Engine — Self-Extraction Mode

**Files:**
- Modify: `src/FalkForge.Engine/Program.cs`

**Step 1: Add self-extraction logic**

When the engine exe has embedded FALKBUNDLE data (i.e., it IS the bundle), it should extract payloads and launch the UI. Update `Program.cs`:

Add a method that checks if the current exe has bundle data appended:

```csharp
private static bool HasEmbeddedBundle()
{
    var exePath = Environment.ProcessPath;
    if (exePath is null) return false;

    using var stream = File.OpenRead(exePath);
    if (stream.Length < 24) return false;

    stream.Seek(-24, SeekOrigin.End);
    Span<byte> footer = stackalloc byte[16];
    stream.ReadExactly(footer);

    ReadOnlySpan<byte> magic = "FALKBUNDLE\0\0\0\0\0\0"u8;
    return footer.SequenceEqual(magic);
}
```

When running as a bundle bootstrapper (no `--manifest` arg but has embedded data):
1. Extract manifest from self using `PayloadEmbedder.Extract()`
2. Extract payloads to cache directory
3. Generate pipe name and shared secret
4. Launch UI process with `--manifest <path> --pipe <name>`
5. Deliver secret via init pipe
6. Run EngineHost with the manifest

**Note:** This requires `FalkForge.Engine` to reference `FalkForge.Compiler.Bundle` for `PayloadEmbedder.Extract()`, OR we extract the bundle reading logic into a shared project. The cleanest approach is to move `PayloadEmbedder.Extract()` and related types to `FalkForge.Engine.Protocol` (which the engine already references).

**Step 2: Implement bootstrapper mode**

At the top of Main():
```csharp
// Bootstrapper mode: if we ARE the bundle, extract and orchestrate
if (manifestPath is null && HasEmbeddedBundle())
{
    return await RunAsBootstrapper(args);
}
```

`RunAsBootstrapper()` handles the full extraction → launch → run flow.

**Step 3: Tests + build**

**Step 4: Commit**

```
feat(engine): add self-extraction bootstrapper mode for bundle exe
```

---

## Task 8: Move Extract Logic to Shared Project

**Files:**
- Move: `PayloadEmbedder.Extract()` logic → `src/FalkForge.Engine.Protocol/Bundle/BundleReader.cs`
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/PayloadEmbedder.cs` — delegate to BundleReader

**Step 1: Create BundleReader in Engine.Protocol**

Move the `Extract()` method, `BundleContent`, and `TocEntry` types to `FalkForge.Engine.Protocol.Bundle.BundleReader` so the engine can read bundles without depending on the compiler.

**Step 2: Update PayloadEmbedder to delegate**

`PayloadEmbedder.Extract()` → calls `BundleReader.Extract()`.

**Step 3: Verify no regressions**

```bash
dotnet build D:/Git/FalkInstaller/FalkForge.slnx
dotnet test D:/Git/FalkInstaller/FalkForge.slnx
```

**Step 4: Commit**

```
refactor: move bundle extraction logic to Engine.Protocol for shared use
```

---

## Task 9: Install Progress Page

**Files:**
- Create: `demo/MAS/Pages/InstallProgressPage.cs`
- Create: `demo/MAS/Views/InstallProgressView.xaml`
- Create: `demo/MAS/Views/InstallProgressView.xaml.cs`

**Step 1: Create the view**

`demo/MAS/Views/InstallProgressView.xaml`:
```xml
<UserControl x:Class="MAS.Views.InstallProgressView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="20,20,20,20">
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="{Binding StatusText}" FontSize="14"
                       HorizontalAlignment="Center" Margin="0,0,0,16" />
            <ProgressBar Value="{Binding ProgressPercent}" Minimum="0" Maximum="100"
                         Height="24" Margin="0,0,0,8" />
            <TextBlock Text="{Binding ProgressDetail}" FontSize="11"
                       Foreground="#666666" HorizontalAlignment="Center" />
        </StackPanel>
    </Grid>
</UserControl>
```

**Step 2: Create the page**

`demo/MAS/Pages/InstallProgressPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class InstallProgressPage : MasPageBase<InstallProgressView>
{
    private string _statusText = string.Empty;
    private int _progressPercent;
    private string _progressDetail = string.Empty;

    public override string Title => Localize("InstallProgress.Title");
    public override bool CanGoBack => false;
    public override bool CanCancel => false;

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetField(ref _progressPercent, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        set => SetField(ref _progressDetail, value);
    }

    public override async Task OnNavigatedToAsync()
    {
        StatusText = Localize("InstallProgress.Installing");

        // Subscribe to engine progress
        Engine.Progress.Subscribe(p =>
        {
            ProgressPercent = p.OverallPercentage;
            ProgressDetail = string.Format(
                Localize("InstallProgress.PackageFormat"),
                p.CurrentPackageDisplayName ?? string.Empty);
        });

        // Plan and apply
        var planResult = await Engine.PlanAsync(InstallAction.Install);
        if (planResult.IsSuccess)
        {
            var applyResult = await Engine.ApplyAsync();
            if (applyResult.IsSuccess)
            {
                SharedState.Set("InstallSuccess", true);
            }
            else
            {
                SharedState.Set("InstallSuccess", false);
                SharedState.Set("InstallError", applyResult.ErrorMessage ?? "Unknown error");
            }
        }
        else
        {
            SharedState.Set("InstallSuccess", false);
            SharedState.Set("InstallError", "Planning failed");
        }

        // Navigate to completion
        NavigateForward();
    }

    public override PageResult OnNext()
    {
        return PageResult.GoTo<CompletionPage>();
    }
}
```

**Step 3: Commit**

```
feat(demo/MAS): add install progress page with engine integration
```

---

## Task 10: Completion Page

**Files:**
- Create: `demo/MAS/Pages/CompletionPage.cs`
- Create: `demo/MAS/Views/CompletionView.xaml`
- Create: `demo/MAS/Views/CompletionView.xaml.cs`

**Step 1: Create the view**

`demo/MAS/Views/CompletionView.xaml`:
```xml
<UserControl x:Class="MAS.Views.CompletionView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="20,20,20,20">
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
            <TextBlock Text="{Binding CompletionMessage}" FontSize="16"
                       TextWrapping="Wrap" TextAlignment="Center" Margin="0,0,0,16" />
            <TextBlock Text="{Binding ErrorDetail}" FontSize="12"
                       Foreground="Red" TextWrapping="Wrap" TextAlignment="Center"
                       Visibility="{Binding ShowError, Converter={StaticResource BoolToVis}}" />
        </StackPanel>
    </Grid>
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis" />
    </UserControl.Resources>
</UserControl>
```

**Step 2: Create the page**

`demo/MAS/Pages/CompletionPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class CompletionPage : MasPageBase<CompletionView>
{
    public override string Title => Localize(
        SharedState.Get<bool>("InstallSuccess")
            ? "Completion.SuccessTitle"
            : "Completion.FailureTitle");

    public override bool CanGoBack => false;
    public override bool ShowNextButton => false;

    public string CompletionMessage => Localize(
        SharedState.Get<bool>("InstallSuccess")
            ? "Completion.SuccessBody"
            : "Completion.FailureBody");

    public string ErrorDetail => SharedState.Get<string>("InstallError") ?? string.Empty;

    public bool ShowError => !SharedState.Get<bool>("InstallSuccess");

    public string FinishButtonText => Localize("Completion.FinishButton");
}
```

**Step 3: Commit**

```
feat(demo/MAS): add completion page showing install success/failure
```

---

## Task 11: Localization Strings

**Files:**
- Modify: `demo/MAS/lang/strings.en-US.json`
- Modify: `demo/MAS/lang/strings.sv-SE.json`

**Step 1: Add new keys**

English (`strings.en-US.json`):
```json
"InstallProgress.Title": "Installing",
"InstallProgress.Installing": "Installing MultiAccess Suite...",
"InstallProgress.PackageFormat": "Installing {0}...",
"Completion.SuccessTitle": "Installation Complete",
"Completion.FailureTitle": "Installation Failed",
"Completion.SuccessBody": "MultiAccess Suite has been successfully installed on your computer.",
"Completion.FailureBody": "The installation could not be completed. Please check the error details below.",
"Completion.FinishButton": "Finish"
```

Swedish (`strings.sv-SE.json`):
```json
"InstallProgress.Title": "Installerar",
"InstallProgress.Installing": "Installerar MultiAccess-sviten...",
"InstallProgress.PackageFormat": "Installerar {0}...",
"Completion.SuccessTitle": "Installationen klar",
"Completion.FailureTitle": "Installationen misslyckades",
"Completion.SuccessBody": "MultiAccess-sviten har installerats på din dator.",
"Completion.FailureBody": "Installationen kunde inte slutföras. Kontrollera felinformationen nedan.",
"Completion.FinishButton": "Slutför"
```

**Step 2: Commit**

```
feat(demo/MAS): add localization for progress and completion pages
```

---

## Task 12: Wire New Pages into MAS Program.cs

**Files:**
- Modify: `demo/MAS/Program.cs`

**Step 1: Add the two new pages**

Add `InstallProgressPage` and `CompletionPage` to the page list in `Program.cs`:

```csharp
.Pages(p => p
    .Add<WelcomePage>()
    .Add<LicensePage>()
    .Add<InstallationTypePage>()
    .Add<DatabaseServerPage>()
    .Add<ConfirmParametersPage>()
    .Add<AdvancedInstallDirMultiServerPage>()
    .Add<AdvancedInstallDirMultiServerExPage>()
    .Add<DatabaseConnectionSettingsPage>()
    .Add<MultiServerAdvancedSettingsPage>()
    .Add<MultiServerExAdvancedSettingsPage>()
    .Add<InstallProgressPage>()
    .Add<CompletionPage>())
```

**Step 2: Update ConfirmParametersPage.OnNext()**

Change the navigation target from the current destination to the progress page:

```csharp
public override PageResult OnNext()
{
    // ... existing SharedState.Set calls ...
    return PageResult.GoTo<InstallProgressPage>();
}
```

**Step 3: Build and verify**

```bash
dotnet build D:/Git/FalkInstaller/demo/MAS/MAS.csproj
```

**Step 4: Commit**

```
feat(demo/MAS): wire progress and completion pages into installer flow
```

---

## Task 13: Integration Verification

**Step 1: Build entire solution**

```bash
dotnet build D:/Git/FalkInstaller/demo/FalkForge.Demos.slnx
```

Expected: 0 errors, 0 warnings.

**Step 2: Run all tests**

```bash
dotnet test D:/Git/FalkInstaller/FalkForge.slnx
```

Expected: All tests pass.

**Step 3: Verify MSI production**

Build each MSI package project and verify `.msi` files are produced:

```bash
for pkg in MultiAccess MultiServer MultiServerEx Konfigurera Concatenate; do
  dotnet run --project D:/Git/FalkInstaller/demo/MAS/packages/$pkg/$pkg.csproj -- -o /tmp/msi-test
  ls -la /tmp/msi-test/$pkg-8.9.0.msi
done
```

**Step 4: Verify MAS design-time mode still works**

```bash
dotnet run --project D:/Git/FalkInstaller/demo/MAS/MAS.csproj
```

MAS should launch normally in design-time mode (NullInstallerEngine) when no `--manifest`/`--pipe` args are provided.

**Step 5: Commit any fixes**

---

## Execution Order

Tasks 1-3 (MSI packages + bundle) are independent of Tasks 4-8 (engine wiring).
Tasks 9-12 (pages + localization) depend on Task 4-5 (engine interface).
Task 13 is final integration.

Recommended batch order:
- **Batch 1:** Tasks 1, 2, 3 (MSI infrastructure)
- **Batch 2:** Tasks 8, 4, 5, 6 (engine wiring — Task 8 first since it unblocks engine self-extraction)
- **Batch 3:** Tasks 7, 9, 10, 11, 12 (bootstrapper + UI pages)
- **Batch 4:** Task 13 (integration verification)
