# 58 - Project References (ProjectOutputs source generator)

Package one project's build output from another project **without hardcoding a
`bin/` path**.

## The problem

A typical installer that packages an application has to point at the app's
compiled output. The naive way is a literal path:

```csharp
files.Add("../app/bin/Debug/net10.0-windows/DemoApp.dll")
```

That path is fragile: it breaks when the configuration changes (Debug/Release),
when the target framework moves, or when output paths are customized. It also
silently goes stale instead of failing the build.

## The solution

The FalkForge MSBuild SDK ships a source generator. When an installer project
has a `ProjectReference` to a project that declares a `FalkOutputType`, the SDK
generates `obj/ProjectOutputs.g.cs` at build time:

```csharp
namespace FalkForge;

internal static class ProjectOutputs
{
    /// <summary>CustomAction output of DemoApp</summary>
    internal const string DemoApp = @"...\app\bin\Debug\net10.0-windows\DemoApp.dll";
}
```

The installer then refers to `ProjectOutputs.DemoApp` instead of a literal path.
The value is resolved by MSBuild, so it always matches the actual build output.

## Three-step setup

### 1. The referenced project exports its output (`app/DemoApp.csproj`)

A plain console app. Two additions make it discoverable:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <!-- Declare what this project produces. CustomAction resolves to
       $(TargetPath) = the primary output assembly. -->
  <FalkOutputType>CustomAction</FalkOutputType>
</PropertyGroup>

<!-- Relative import of the FalkForge SDK targets (the SDK is NOT
     NuGet-packaged). Brings in _GetFalkForgeOutput / _ComputeFalkArtifactPath
     so a referencing project can query the artifact path. We import
     Sdk.targets only; Sdk.props would default FalkOutputType to Msi. -->
<Import Project="../../../src/FalkForge.Sdk/Sdk/Sdk.targets" />
```

`FalkOutputType` values and what they resolve to:

| FalkOutputType | Resolved path |
|----------------|---------------|
| `Msi`          | `$(TargetDir)$(AssemblyName).msi` |
| `Bundle`       | `$(TargetDir)$(AssemblyName).exe` |
| `Module`       | `$(TargetDir)$(AssemblyName).msm` |
| `Patch`        | `$(TargetDir)$(AssemblyName).msp` |
| `CustomAction` | `$(TargetPath)` (the primary output assembly) |
| `Msix` / `MsixBundle` | `.msix` / `.msixbundle` |

There is **no** `Exe`/`App` type. For a plain application whose compiled output
you want to package, `CustomAction` is the right choice because it exports
`$(TargetPath)`.

### 2. The installer references the app with `ReferenceOutputAssembly="false"`

`installer/installer.csproj`:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-windows</TargetFramework>
  <!-- This project is run manually with -o (like every other demo), so
       disable the SDK's after-build auto-generation step. -->
  <FalkOutputType>None</FalkOutputType>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="../../../src/FalkForge.Core/FalkForge.Core.csproj" />
  <ProjectReference Include="../../../src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj" />
  <!-- ReferenceOutputAssembly="false": we do NOT link against DemoApp at
       compile time; we only want its build OUTPUT PATH. -->
  <ProjectReference Include="../app/DemoApp.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>

<!-- _GenerateProjectOutputs runs BeforeTargets=CoreCompile whenever a
     ProjectReference exists, emitting obj/ProjectOutputs.g.cs. -->
<Import Project="../../../src/FalkForge.Sdk/Sdk/Sdk.targets" />
```

### 3. Use the generated constant (`installer/Program.cs`)

The recommended approach is to harvest the **whole output directory**. Because
`ProjectOutputs.DemoApp` resolves to `$(TargetPath)` (the managed `.dll`),
`Path.GetDirectoryName` gives the app's output folder, which contains the
apphost `.exe`, runtime config, deps, and any other build artifacts:

```csharp
// ProjectOutputs.DemoApp is a compile-time constant holding the RESOLVED
// path to the app's primary output (DemoApp.dll) — no hardcoded bin/ path.
// Deriving its directory and harvesting it packages EVERYTHING the app
// builds (exe + dll + runtimeconfig.json + deps.json), still with no
// hardcoded path: the directory follows the resolved artifact path.
var appOutputDir = Path.GetDirectoryName(ProjectOutputs.DemoApp)!;
package.Files(files => files
    .FromDirectory(appOutputDir)
    .To(KnownFolder.ProgramFiles / "DemoApp"));
```

If you only need the single managed assembly, use the simpler single-file form:

```csharp
// Alternative: package just the single resolved artifact.
package.Files(files => files
    .Add(ProjectOutputs.DemoApp)
    .To(KnownFolder.ProgramFiles / "DemoApp"));
```

## Naming rule

The generated member name is the referenced project's name
(`$(MSBuildProjectName)`) sanitized to a valid C# identifier:

- `.`, `-`, space and any other non-`[A-Za-z0-9_]` character become `_`
- a leading digit gets an `_` prefix

So `DemoApp` stays `DemoApp`. A project named `My.Cool-App 2` would become
`My_Cool_App_2`.

## Build and run

```bash
# 1. Build the app (produces DemoApp.exe + DemoApp.dll + supporting files).
dotnet build app/DemoApp.csproj

# 2. Build the installer. This generates obj/.../ProjectOutputs.g.cs.
dotnet build installer/installer.csproj

# 3. Run the installer to produce the MSI (manual -o, like the other demos).
#    The compiler treats -o as a base directory and emits:
#      <base>/<PackageName>-<Version>.msi
#    e.g. out/Project_References_Demo-1.0.0.msi
dotnet run --project installer/installer.csproj -- -o ./out/

# 4. Prove the MSI contains the full app output directory.
dotnet run --project ../../src/FalkForge.Cli/FalkForge.Cli.csproj -- \
    extract ./out/Project_References_Demo-1.0.0.msi -o ./out/verify
```

## Verified output

This sample was built and run end to end:

- `ProjectOutputs.g.cs` is generated with `internal const string DemoApp = @"...\DemoApp.dll";`
- Both builds are 0 warnings / 0 errors.
- The produced MSI (`Project_References_Demo-1.0.0.msi`), when extracted,
  contains 5 files under `ProgramFiles/DemoApp/`:

  ```
  DemoApp.deps.json
  DemoApp.dll
  DemoApp.exe
  DemoApp.pdb
  DemoApp.runtimeconfig.json
  ```

  The whole build output directory is packaged — including the apphost `.exe`
  — with no hardcoded `bin/` path anywhere.

## How `FalkOutputType=CustomAction` relates to the directory harvest

`CustomAction` exports `$(TargetPath)`, which on modern .NET is the managed
assembly (`DemoApp.dll`). The apphost `DemoApp.exe` and supporting files live
in the same output directory. `Path.GetDirectoryName(ProjectOutputs.DemoApp)`
gives that directory, and `FromDirectory` picks up every file in it. You get
both the `.dll` and the `.exe` (and everything else) — still without any
hardcoded path.
