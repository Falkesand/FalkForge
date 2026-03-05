# High-Value Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Complete the 3 remaining high-value features: (1) Bundle signing demo, (2) Feature state migration on upgrades, (3) Dependency extension runtime enforcement.

**Architecture:** Feature state migration adds cross-bundle registry lookup during detection to inherit feature selections from superseded bundles. Dependency runtime enforcement adds version range matching to the existing DependencyDetector. The signing demo showcases the existing detach/reattach API.

**Tech Stack:** C#, .NET 10, xUnit, Spectre.Console CLI, Windows Registry

---

## Feature A: Bundle Detach/Reattach Signing Demo

The CLI commands (`forge bundle detach` / `forge bundle reattach`) already exist. This task adds a demo project showing the full workflow.

### Task A1: Create signing demo project

**Files:**
- Create: `demo/15-bundle-signing/bundle/Program.cs`
- Create: `demo/15-bundle-signing/bundle/bundle.csproj`
- Create: `demo/15-bundle-signing/msi-package/Program.cs`
- Create: `demo/15-bundle-signing/msi-package/msi-package.csproj`
- Create: `demo/15-bundle-signing/msi-package/payload/app.exe`

**Step 1: Create MSI package project**

Create `demo/15-bundle-signing/msi-package/msi-package.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\FalkForge.Core\FalkForge.Core.csproj" />
    <ProjectReference Include="..\..\..\src\FalkForge.Compiler.Msi\FalkForge.Compiler.Msi.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Create `demo/15-bundle-signing/msi-package/payload/app.exe` as a zero-byte placeholder.

Create `demo/15-bundle-signing/msi-package/Program.cs`:
```csharp
using FalkForge.Core;
using FalkForge.Core.Builders;

var output = Path.Combine(AppContext.BaseDirectory, "MyApp.msi");

var result = Installer.BuildMsi(output, package =>
{
    package
        .Name("SigningDemo")
        .Version("1.0.0")
        .Manufacturer("FalkForge")
        .UpgradeCode(new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"))
        .AddDirectory("INSTALLFOLDER", "SigningDemo", dir =>
        {
            dir.AddFile(Path.Combine(AppContext.BaseDirectory, "payload", "app.exe"));
        });
});

return result.IsSuccess ? 0 : 1;
```

**Step 2: Create bundle project**

Create `demo/15-bundle-signing/bundle/bundle.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\FalkForge.Core\FalkForge.Core.csproj" />
    <ProjectReference Include="..\..\..\src\FalkForge.Compiler.Bundle\FalkForge.Compiler.Bundle.csproj" />
    <ProjectReference Include="..\msi-package\msi-package.csproj" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

Create `demo/15-bundle-signing/bundle/Program.cs`:
```csharp
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Step 1: Build the bundle
var msiPath = Path.Combine(AppContext.BaseDirectory, "..", "msi-package",
    "bin", "Debug", "net10.0-windows", "MyApp.msi");
var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDir);

var bundlePath = Path.Combine(outputDir, "MyApp-Setup.exe");

var bundle = new BundleBuilder()
    .Name("SigningDemo Bundle")
    .Version("1.0.0")
    .UpgradeCode(new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901"))
    .Chain(chain =>
    {
        chain.MsiPackage("MainApp", msiPath);
    });

var compileResult = new BundleCompiler().Compile(bundle.Build(), bundlePath);
if (compileResult.IsFailure)
{
    Console.Error.WriteLine($"Bundle compile failed: {compileResult.Error.Message}");
    return 1;
}

Console.WriteLine($"Bundle created: {bundlePath}");

// Step 2: Detach for signing
var stubPath = Path.Combine(outputDir, "stub.exe");
var dataPath = Path.Combine(outputDir, "bundle.dat");

var detachResult = BundleDetacher.Detach(bundlePath, stubPath, dataPath);
if (detachResult.IsFailure)
{
    Console.Error.WriteLine($"Detach failed: {detachResult.Error.Message}");
    return 2;
}

Console.WriteLine($"Detached: stub={stubPath}, data={dataPath}");
Console.WriteLine(">>> Sign stub.exe with your code signing certificate here <<<");
Console.WriteLine(">>> e.g.: signtool sign /fd SHA256 /a stub.exe <<<");

// Step 3: Reattach after signing
var signedBundlePath = Path.Combine(outputDir, "MyApp-Setup-Signed.exe");

var reattachResult = BundleDetacher.Reattach(stubPath, dataPath, signedBundlePath);
if (reattachResult.IsFailure)
{
    Console.Error.WriteLine($"Reattach failed: {reattachResult.Error.Message}");
    return 3;
}

Console.WriteLine($"Signed bundle: {signedBundlePath}");
return 0;
```

**Step 3: Build and verify**

Run: `dotnet build demo/15-bundle-signing/msi-package/msi-package.csproj`
Run: `dotnet build demo/15-bundle-signing/bundle/bundle.csproj`
Expected: Both compile without errors or warnings.

**Step 4: Commit**

```bash
git add demo/15-bundle-signing/
git commit -m "demo: add bundle signing demo (detach/reattach workflow)"
```

---

## Feature B: Feature State Migration on Upgrades

### Problem

When upgrading from BundleA v1.0 to BundleB v2.0, the new bundle has a different `BundleId`. `FeaturePersistence` stores selections keyed by `BundleId`, so the new bundle doesn't find the old bundle's feature choices. Users lose their feature selections on upgrade.

### Solution

During detection, when related bundles are found with `Relation == Upgrade`, load feature selections from each related bundle's registry key. Merge into the current detection as `WasPreviouslyInstalled` defaults.

### Task B1: Add FeaturePersistence.LoadFromRelatedBundles

**Files:**
- Modify: `src/FalkForge.Engine/Variables/FeaturePersistence.cs`
- Test: `tests/FalkForge.Engine.Tests/Variables/FeaturePersistenceTests.cs`

**Step 1: Write the failing test**

Add to `FeaturePersistenceTests.cs`:
```csharp
[Fact]
public void LoadFromRelatedBundle_ReturnsSelections_WhenRelatedBundleHasSavedState()
{
    var registry = new MockRegistry();
    var relatedBundleId = new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
    var selections = new Dictionary<string, bool>
    {
        ["core"] = true,
        ["docs"] = false,
        ["extras"] = true
    };

    // Save selections under the related (old) bundle's key
    FeaturePersistence.SaveFeatureSelections(
        registry, relatedBundleId, InstallScope.PerUser, selections);

    var newFeatures = new ManifestFeature[]
    {
        MakeFeature("core"),
        MakeFeature("docs"),
        MakeFeature("newfeature") // new feature not in old bundle
    };

    var result = FeaturePersistence.LoadFromRelatedBundle(
        registry, relatedBundleId, InstallScope.PerUser, newFeatures);

    Assert.Equal(2, result.Count); // only core and docs found
    Assert.True(result["core"]);
    Assert.False(result["docs"]);
    Assert.False(result.ContainsKey("newfeature")); // new feature not in old data
}

[Fact]
public void LoadFromRelatedBundle_ReturnsEmpty_WhenNoSavedState()
{
    var registry = new MockRegistry();
    var relatedBundleId = new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
    var features = new ManifestFeature[] { MakeFeature("core") };

    var result = FeaturePersistence.LoadFromRelatedBundle(
        registry, relatedBundleId, InstallScope.PerUser, features);

    Assert.Empty(result);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FeaturePersistenceTests.LoadFromRelatedBundle"`
Expected: FAIL — method does not exist.

**Step 3: Write minimal implementation**

Add to `FeaturePersistence.cs`:
```csharp
/// <summary>
/// Loads feature selections saved by a related (superseded) bundle.
/// Returns only features that exist in both the old saved state and the new feature list.
/// </summary>
public static Dictionary<string, bool> LoadFromRelatedBundle(
    IRegistry registry,
    Guid relatedBundleId,
    InstallScope scope,
    IReadOnlyList<ManifestFeature> currentFeatures)
{
    return LoadFeatureSelections(registry, relatedBundleId, scope, currentFeatures);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FeaturePersistenceTests.LoadFromRelatedBundle"`
Expected: PASS

**Step 5: Commit**

```bash
git add src/FalkForge.Engine/Variables/FeaturePersistence.cs tests/FalkForge.Engine.Tests/Variables/FeaturePersistenceTests.cs
git commit -m "feat(Engine): add FeaturePersistence.LoadFromRelatedBundle for upgrade state migration"
```

---

### Task B2: Wire feature migration into FeatureDetector

**Files:**
- Modify: `src/FalkForge.Engine/Detection/FeatureDetector.cs`
- Test: `tests/FalkForge.Engine.Tests/Detection/FeatureDetectorTests.cs`

**Step 1: Write the failing test**

Add to `FeatureDetectorTests.cs` (create if needed):
```csharp
[Fact]
public void Detect_InheritsSelectionsFromRelatedBundle_WhenUpgrading()
{
    var registry = new MockRegistry();
    var currentBundleId = Guid.NewGuid();
    var oldBundleId = new Guid("11111111-2222-3333-4444-555555555555");

    // Old bundle had features: core=true, docs=false
    FeaturePersistence.SaveFeatureSelections(
        registry, oldBundleId, InstallScope.PerUser,
        new Dictionary<string, bool> { ["core"] = true, ["docs"] = false });

    var features = new ManifestFeature[]
    {
        new("core", "Core", null, true, false, []),
        new("docs", "Docs", null, true, false, []),   // default=true but old had false
        new("extras", "Extras", null, false, false, []) // new feature, default=false
    };

    var relatedBundles = new[] { new RelatedBundleInfo
    {
        BundleId = oldBundleId.ToString("B"),
        InstalledVersion = "1.0.0",
        Relation = RelatedBundleRelation.Upgrade,
        RegistryKeyPath = ""
    }};

    var result = FeatureDetector.Detect(
        features, registry, currentBundleId, InstallScope.PerUser,
        new Dictionary<string, InstallState>(),
        relatedBundles);

    // core: inherited true from old bundle
    Assert.True(result[0].IsSelected);
    Assert.True(result[0].WasPreviouslyInstalled);

    // docs: inherited false from old bundle (overrides default=true)
    Assert.False(result[1].IsSelected);
    Assert.True(result[1].WasPreviouslyInstalled);

    // extras: new feature, uses default=false
    Assert.False(result[2].IsSelected);
    Assert.False(result[2].WasPreviouslyInstalled);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FeatureDetectorTests.Detect_InheritsSelectionsFromRelatedBundle"`
Expected: FAIL — `FeatureDetector.Detect` doesn't accept `relatedBundles` parameter.

**Step 3: Add relatedBundles parameter to FeatureDetector.Detect**

Modify `FeatureDetector.cs` — add an overload that accepts related bundles:
```csharp
public static FeatureState[] Detect(
    ManifestFeature[] features,
    IRegistry registry,
    Guid bundleId,
    InstallScope scope,
    Dictionary<string, InstallState> packageResults,
    IReadOnlyList<RelatedBundleInfo>? relatedBundles = null)
{
    if (features.Length == 0)
        return [];

    // Phase 1: Try loading from current bundle's registry
    var registrySelections = FeaturePersistence.LoadFeatureSelections(registry, bundleId, scope, features);
    var anyFoundInRegistry = registrySelections.Count > 0;

    // Phase 1b: If no current selections found, try related bundles (upgrade migration)
    Dictionary<string, bool>? migratedSelections = null;
    if (!anyFoundInRegistry && relatedBundles is { Count: > 0 })
    {
        foreach (var related in relatedBundles)
        {
            if (related.Relation != RelatedBundleRelation.Upgrade)
                continue;

            if (!Guid.TryParse(related.BundleId, out var relatedGuid))
                continue;

            migratedSelections = FeaturePersistence.LoadFromRelatedBundle(
                registry, relatedGuid, scope, features);

            if (migratedSelections.Count > 0)
                break; // use first related bundle with saved state
        }
    }

    // Phase 2: MSI fallback (only if no registry or migration data)
    var msiInferredSelections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    if (!anyFoundInRegistry && (migratedSelections is null || migratedSelections.Count == 0))
    {
        foreach (var feature in features)
        {
            if (feature.PackageIds.Length == 0)
                continue;

            var allInstalled = true;
            foreach (var packageId in feature.PackageIds)
            {
                if (packageResults.TryGetValue(packageId, out var state))
                {
                    if (state is not (InstallState.Installed or InstallState.OlderVersion))
                    {
                        allInstalled = false;
                        break;
                    }
                }
                else
                {
                    allInstalled = false;
                    break;
                }
            }

            msiInferredSelections[feature.Id] = allInstalled;
        }
    }

    // Build FeatureState array
    var result = new FeatureState[features.Length];
    for (var i = 0; i < features.Length; i++)
    {
        var feature = features[i];
        bool isSelected;
        bool wasPreviouslyInstalled;

        if (anyFoundInRegistry && registrySelections.TryGetValue(feature.Id, out var regSelected))
        {
            isSelected = regSelected;
            wasPreviouslyInstalled = true;
        }
        else if (migratedSelections is not null && migratedSelections.TryGetValue(feature.Id, out var migrated))
        {
            isSelected = migrated;
            wasPreviouslyInstalled = true;
        }
        else if (!anyFoundInRegistry && msiInferredSelections.TryGetValue(feature.Id, out var msiSelected))
        {
            isSelected = msiSelected;
            wasPreviouslyInstalled = msiSelected;
        }
        else
        {
            isSelected = feature.IsDefault;
            wasPreviouslyInstalled = false;
        }

        if (feature.IsRequired)
            isSelected = true;

        result[i] = new FeatureState(
            feature.Id,
            feature.Title,
            feature.Description,
            isSelected,
            feature.IsRequired,
            wasPreviouslyInstalled,
            DiskSpaceRequired: 0);
    }

    return result;
}
```

**Step 4: Update DetectingHandler to pass related bundles**

Modify `DetectingHandler.cs` — pass `context.DetectedRelatedBundles` to `FeatureDetector.Detect`:
```csharp
// Change the FeatureDetector.Detect call (around line 75) to:
var features = FeatureDetector.Detect(
    context.Manifest.Features,
    context.Platform.Registry,
    context.Manifest.BundleId,
    context.Manifest.Scope,
    perPackageStates,
    context.DetectedRelatedBundles);
```

**Note:** The related bundles detection happens before features (line 82-86 in DetectingHandler), so `context.DetectedRelatedBundles` is already populated.

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FeatureDetectorTests"`
Expected: PASS (both new and existing tests)

**Step 6: Commit**

```bash
git add src/FalkForge.Engine/Detection/FeatureDetector.cs src/FalkForge.Engine/Phases/DetectingHandler.cs tests/FalkForge.Engine.Tests/Detection/FeatureDetectorTests.cs
git commit -m "feat(Engine): migrate feature selections from related bundle during upgrade detection"
```

---

### Task B3: Add edge case tests for feature migration

**Files:**
- Test: `tests/FalkForge.Engine.Tests/Detection/FeatureDetectorTests.cs`

**Step 1: Write additional tests**

```csharp
[Fact]
public void Detect_PrefersCurrent_OverRelated_WhenBothExist()
{
    var registry = new MockRegistry();
    var currentBundleId = Guid.NewGuid();
    var oldBundleId = new Guid("11111111-2222-3333-4444-555555555555");

    // Current bundle has saved state (e.g., modify scenario)
    FeaturePersistence.SaveFeatureSelections(
        registry, currentBundleId, InstallScope.PerUser,
        new Dictionary<string, bool> { ["core"] = false });

    // Old bundle also has saved state
    FeaturePersistence.SaveFeatureSelections(
        registry, oldBundleId, InstallScope.PerUser,
        new Dictionary<string, bool> { ["core"] = true });

    var features = new ManifestFeature[]
    {
        new("core", "Core", null, true, false, [])
    };

    var relatedBundles = new[] { new RelatedBundleInfo
    {
        BundleId = oldBundleId.ToString("B"),
        InstalledVersion = "1.0.0",
        Relation = RelatedBundleRelation.Upgrade,
        RegistryKeyPath = ""
    }};

    var result = FeatureDetector.Detect(
        features, registry, currentBundleId, InstallScope.PerUser,
        new Dictionary<string, InstallState>(), relatedBundles);

    // Current bundle's state takes precedence
    Assert.False(result[0].IsSelected);
}

[Fact]
public void Detect_IgnoresNonUpgradeRelatedBundles()
{
    var registry = new MockRegistry();
    var currentBundleId = Guid.NewGuid();
    var addonBundleId = new Guid("22222222-3333-4444-5555-666666666666");

    FeaturePersistence.SaveFeatureSelections(
        registry, addonBundleId, InstallScope.PerUser,
        new Dictionary<string, bool> { ["core"] = true });

    var features = new ManifestFeature[]
    {
        new("core", "Core", null, false, false, []) // default=false
    };

    var relatedBundles = new[] { new RelatedBundleInfo
    {
        BundleId = addonBundleId.ToString("B"),
        InstalledVersion = "1.0.0",
        Relation = RelatedBundleRelation.Addon, // NOT Upgrade
        RegistryKeyPath = ""
    }};

    var result = FeatureDetector.Detect(
        features, registry, currentBundleId, InstallScope.PerUser,
        new Dictionary<string, InstallState>(), relatedBundles);

    // Addon relation ignored — uses default
    Assert.False(result[0].IsSelected);
    Assert.False(result[0].WasPreviouslyInstalled);
}

[Fact]
public void Detect_RequiredFeature_AlwaysSelected_EvenWhenMigratedAsFalse()
{
    var registry = new MockRegistry();
    var currentBundleId = Guid.NewGuid();
    var oldBundleId = new Guid("33333333-4444-5555-6666-777777777777");

    // Old bundle had core=false (was optional then, now required)
    FeaturePersistence.SaveFeatureSelections(
        registry, oldBundleId, InstallScope.PerUser,
        new Dictionary<string, bool> { ["core"] = false });

    var features = new ManifestFeature[]
    {
        new("core", "Core", null, true, true, []) // IsRequired=true
    };

    var relatedBundles = new[] { new RelatedBundleInfo
    {
        BundleId = oldBundleId.ToString("B"),
        InstalledVersion = "1.0.0",
        Relation = RelatedBundleRelation.Upgrade,
        RegistryKeyPath = ""
    }};

    var result = FeatureDetector.Detect(
        features, registry, currentBundleId, InstallScope.PerUser,
        new Dictionary<string, InstallState>(), relatedBundles);

    Assert.True(result[0].IsSelected); // Required overrides migration
    Assert.True(result[0].WasPreviouslyInstalled);
}
```

**Step 2: Run all feature detector tests**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "FeatureDetectorTests"`
Expected: All PASS

**Step 3: Commit**

```bash
git add tests/FalkForge.Engine.Tests/Detection/FeatureDetectorTests.cs
git commit -m "test(Engine): add edge case tests for feature state migration"
```

---

## Feature C: Dependency Extension Runtime Enforcement

### Problem

The Dependency extension validates at build-time (DependencyValidator) and emits registry entries (DependencyTableContributor). But during install, the engine's `DependencyDetector` only checks for **blocking dependents** (preventing uninstall). It doesn't check whether **required dependencies are satisfied** — i.e., whether a consumer's version range requirement is met by the installed provider.

### Task C1: Add VersionRange matching

**Files:**
- Create: `src/FalkForge.Extensions.Dependency/VersionRange.cs`
- Test: `tests/FalkForge.Extensions.Dependency.Tests/VersionRangeTests.cs`

**Step 1: Write the failing tests**

Create `tests/FalkForge.Extensions.Dependency.Tests/VersionRangeTests.cs`:
```csharp
using FalkForge.Extensions.Dependency;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("1.0.0", null, null, true, false, true)]   // no range = any version
    [InlineData("1.0.0", "1.0.0", null, true, false, true)] // min inclusive match
    [InlineData("0.9.0", "1.0.0", null, true, false, false)] // below min
    [InlineData("1.0.0", "1.0.0", null, false, false, false)] // min exclusive, at boundary
    [InlineData("1.0.1", "1.0.0", null, false, false, true)] // min exclusive, above
    [InlineData("2.0.0", null, "2.0.0", true, true, true)]  // max inclusive match
    [InlineData("2.0.1", null, "2.0.0", true, true, false)] // above max
    [InlineData("2.0.0", null, "2.0.0", true, false, false)] // max exclusive, at boundary
    [InlineData("1.9.9", null, "2.0.0", true, false, true)] // max exclusive, below
    [InlineData("1.5.0", "1.0.0", "2.0.0", true, false, true)] // in range
    [InlineData("0.5.0", "1.0.0", "2.0.0", true, false, false)] // below range
    [InlineData("2.5.0", "1.0.0", "2.0.0", true, false, false)] // above range
    public void IsSatisfiedBy_ReturnsExpected(
        string version, string? min, string? max,
        bool minInclusive, bool maxInclusive, bool expected)
    {
        var range = new VersionRange(min, max, minInclusive, maxInclusive);
        Assert.Equal(expected, range.IsSatisfiedBy(version));
    }

    [Fact]
    public void IsSatisfiedBy_ReturnsFalse_WhenVersionIsInvalid()
    {
        var range = new VersionRange("1.0.0", null, true, false);
        Assert.False(range.IsSatisfiedBy("not-a-version"));
    }

    [Fact]
    public void IsSatisfiedBy_ReturnsFalse_WhenVersionIsNull()
    {
        var range = new VersionRange("1.0.0", null, true, false);
        Assert.False(range.IsSatisfiedBy(null!));
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Dependency.Tests/FalkForge.Extensions.Dependency.Tests.csproj --filter "VersionRangeTests"`
Expected: FAIL — class does not exist.

**Step 3: Write implementation**

Create `src/FalkForge.Extensions.Dependency/VersionRange.cs`:
```csharp
namespace FalkForge.Extensions.Dependency;

/// <summary>
/// Represents a version range with optional min/max bounds and inclusive/exclusive semantics.
/// </summary>
public readonly record struct VersionRange(
    string? MinVersion,
    string? MaxVersion,
    bool MinInclusive,
    bool MaxInclusive)
{
    /// <summary>
    /// Returns true if the given version string satisfies this range.
    /// </summary>
    public bool IsSatisfiedBy(string? version)
    {
        if (version is null || !Version.TryParse(version, out var ver))
            return false;

        if (MinVersion is not null && Version.TryParse(MinVersion, out var min))
        {
            var cmp = ver.CompareTo(min);
            if (MinInclusive ? cmp < 0 : cmp <= 0)
                return false;
        }

        if (MaxVersion is not null && Version.TryParse(MaxVersion, out var max))
        {
            var cmp = ver.CompareTo(max);
            if (MaxInclusive ? cmp > 0 : cmp >= 0)
                return false;
        }

        return true;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Dependency.Tests/FalkForge.Extensions.Dependency.Tests.csproj --filter "VersionRangeTests"`
Expected: All PASS

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Dependency/VersionRange.cs tests/FalkForge.Extensions.Dependency.Tests/VersionRangeTests.cs
git commit -m "feat(Dependency): add VersionRange with inclusive/exclusive bound matching"
```

---

### Task C2: Add DependencyChecker for runtime provider version validation

**Files:**
- Create: `src/FalkForge.Extensions.Dependency/DependencyChecker.cs`
- Test: `tests/FalkForge.Extensions.Dependency.Tests/DependencyCheckerTests.cs`

**Step 1: Write the failing tests**

Create `tests/FalkForge.Extensions.Dependency.Tests/DependencyCheckerTests.cs`:
```csharp
using FalkForge.Extensions.Dependency;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyCheckerTests
{
    [Fact]
    public void Check_ReturnsEmpty_WhenNoConsumers()
    {
        var consumers = Array.Empty<DependencyConsumerModel>();
        var installedProviders = new Dictionary<string, string>(); // key -> version

        var result = DependencyChecker.Check(consumers, installedProviders);

        Assert.Empty(result);
    }

    [Fact]
    public void Check_ReturnsUnsatisfied_WhenProviderNotInstalled()
    {
        var consumers = new[]
        {
            new DependencyConsumerModel
            {
                ProviderKey = "MyApp.Runtime",
                ConsumerKey = "MyApp.Plugin",
                MinVersion = "1.0.0"
            }
        };
        var installedProviders = new Dictionary<string, string>(); // nothing installed

        var result = DependencyChecker.Check(consumers, installedProviders);

        Assert.Single(result);
        Assert.Equal("MyApp.Runtime", result[0].ProviderKey);
        Assert.Equal("MyApp.Plugin", result[0].ConsumerKey);
        Assert.True(result[0].IsMissing);
    }

    [Fact]
    public void Check_ReturnsUnsatisfied_WhenVersionOutOfRange()
    {
        var consumers = new[]
        {
            new DependencyConsumerModel
            {
                ProviderKey = "MyApp.Runtime",
                ConsumerKey = "MyApp.Plugin",
                MinVersion = "2.0.0"
            }
        };
        var installedProviders = new Dictionary<string, string>
        {
            ["MyApp.Runtime"] = "1.5.0"
        };

        var result = DependencyChecker.Check(consumers, installedProviders);

        Assert.Single(result);
        Assert.False(result[0].IsMissing);
        Assert.Equal("1.5.0", result[0].InstalledVersion);
    }

    [Fact]
    public void Check_ReturnsSatisfied_WhenVersionInRange()
    {
        var consumers = new[]
        {
            new DependencyConsumerModel
            {
                ProviderKey = "MyApp.Runtime",
                ConsumerKey = "MyApp.Plugin",
                MinVersion = "1.0.0",
                MaxVersion = "3.0.0"
            }
        };
        var installedProviders = new Dictionary<string, string>
        {
            ["MyApp.Runtime"] = "2.0.0"
        };

        var result = DependencyChecker.Check(consumers, installedProviders);

        Assert.Empty(result);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Dependency.Tests/FalkForge.Extensions.Dependency.Tests.csproj --filter "DependencyCheckerTests"`
Expected: FAIL — class does not exist.

**Step 3: Write implementation**

Create `src/FalkForge.Extensions.Dependency/DependencyChecker.cs`:
```csharp
namespace FalkForge.Extensions.Dependency;

/// <summary>
/// Runtime check: verifies that installed dependency providers satisfy consumer version requirements.
/// </summary>
public static class DependencyChecker
{
    /// <summary>
    /// Checks whether all consumer requirements are met by the installed providers.
    /// Returns list of unsatisfied dependencies.
    /// </summary>
    /// <param name="consumers">Consumer requirements to check.</param>
    /// <param name="installedProviders">Map of provider key → installed version string.</param>
    public static IReadOnlyList<UnsatisfiedDependency> Check(
        IReadOnlyList<DependencyConsumerModel> consumers,
        IReadOnlyDictionary<string, string> installedProviders)
    {
        if (consumers.Count == 0)
            return [];

        var results = new List<UnsatisfiedDependency>();

        foreach (var consumer in consumers)
        {
            if (!installedProviders.TryGetValue(consumer.ProviderKey, out var installedVersion))
            {
                results.Add(new UnsatisfiedDependency(
                    consumer.ProviderKey,
                    consumer.ConsumerKey,
                    InstalledVersion: null,
                    IsMissing: true));
                continue;
            }

            var range = new VersionRange(
                consumer.MinVersion,
                consumer.MaxVersion,
                consumer.MinInclusive,
                consumer.MaxInclusive);

            if (!range.IsSatisfiedBy(installedVersion))
            {
                results.Add(new UnsatisfiedDependency(
                    consumer.ProviderKey,
                    consumer.ConsumerKey,
                    installedVersion,
                    IsMissing: false));
            }
        }

        return results;
    }
}

/// <summary>
/// Represents a dependency requirement that is not satisfied.
/// </summary>
public sealed record UnsatisfiedDependency(
    string ProviderKey,
    string ConsumerKey,
    string? InstalledVersion,
    bool IsMissing);
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Dependency.Tests/FalkForge.Extensions.Dependency.Tests.csproj --filter "DependencyCheckerTests"`
Expected: All PASS

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Dependency/DependencyChecker.cs src/FalkForge.Extensions.Dependency/UnsatisfiedDependency.cs tests/FalkForge.Extensions.Dependency.Tests/DependencyCheckerTests.cs
git commit -m "feat(Dependency): add DependencyChecker for runtime provider version validation"
```

**Note:** If the record is in the same file as DependencyChecker, no separate file needed. Follow the one-class-per-file rule — put UnsatisfiedDependency in its own file.

---

### Task C3: Wire DependencyChecker into DependencyDetector

**Files:**
- Modify: `src/FalkForge.Engine/Detection/DependencyDetector.cs`
- Create: `src/FalkForge.Engine/Detection/DependencyRequirement.cs`
- Test: `tests/FalkForge.Engine.Tests/Detection/DependencyDetectorTests.cs`

**Step 1: Write the failing test**

Add to or create `DependencyDetectorTests.cs`:
```csharp
[Fact]
public void DetectUnsatisfiedProviders_ReturnsEmpty_WhenProviderVersionSatisfied()
{
    var registry = new MockRegistry();
    // Simulate installed provider: MyApp.Runtime version 2.0.0
    registry.SetStringValue("HKLM",
        @"SOFTWARE\Classes\Installer\Dependencies\MyApp.Runtime",
        "", "MyApp.Runtime");
    registry.SetStringValue("HKLM",
        @"SOFTWARE\Classes\Installer\Dependencies\MyApp.Runtime",
        "Version", "2.0.0");

    var requirements = new[]
    {
        new ManifestDependencyRequirement("MyApp.Runtime", "1.0.0", null, true, false)
    };

    var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

    Assert.Empty(result);
}

[Fact]
public void DetectUnsatisfiedProviders_ReturnsUnsatisfied_WhenProviderVersionTooLow()
{
    var registry = new MockRegistry();
    registry.SetStringValue("HKLM",
        @"SOFTWARE\Classes\Installer\Dependencies\MyApp.Runtime",
        "", "MyApp.Runtime");
    registry.SetStringValue("HKLM",
        @"SOFTWARE\Classes\Installer\Dependencies\MyApp.Runtime",
        "Version", "1.0.0");

    var requirements = new[]
    {
        new ManifestDependencyRequirement("MyApp.Runtime", "2.0.0", null, true, false)
    };

    var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

    Assert.Single(result);
    Assert.Equal("MyApp.Runtime", result[0].ProviderKey);
}

[Fact]
public void DetectUnsatisfiedProviders_ReturnsMissing_WhenProviderNotInstalled()
{
    var registry = new MockRegistry();

    var requirements = new[]
    {
        new ManifestDependencyRequirement("Missing.Provider", "1.0.0", null, true, false)
    };

    var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

    Assert.Single(result);
    Assert.True(result[0].IsMissing);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "DependencyDetectorTests.DetectUnsatisfiedProviders"`
Expected: FAIL — method and types don't exist.

**Step 3: Create ManifestDependencyRequirement**

Create `src/FalkForge.Engine.Protocol/Manifest/ManifestDependencyRequirement.cs`:
```csharp
namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestDependencyRequirement(
    string ProviderKey,
    string? MinVersion,
    string? MaxVersion,
    bool MinInclusive,
    bool MaxInclusive);
```

**Step 4: Add DetectUnsatisfiedProviders to DependencyDetector**

Add to `DependencyDetector.cs`:
```csharp
/// <summary>
/// Checks whether required dependency providers are installed with satisfactory versions.
/// Reads the provider Version value from the registry.
/// </summary>
internal static IReadOnlyList<UnsatisfiedProviderInfo> DetectUnsatisfiedProviders(
    IReadOnlyList<ManifestDependencyRequirement> requirements,
    IRegistry registry)
{
    if (requirements.Count == 0)
        return [];

    var results = new List<UnsatisfiedProviderInfo>();

    foreach (var req in requirements)
    {
        var basePath = $@"SOFTWARE\Classes\Installer\Dependencies\{req.ProviderKey}";
        var installedVersion = registry.GetStringValue("HKLM", basePath, "Version");

        if (installedVersion is null)
        {
            results.Add(new UnsatisfiedProviderInfo(req.ProviderKey, null, IsMissing: true));
            continue;
        }

        var range = new Extensions.Dependency.VersionRange(
            req.MinVersion, req.MaxVersion, req.MinInclusive, req.MaxInclusive);

        if (!range.IsSatisfiedBy(installedVersion))
        {
            results.Add(new UnsatisfiedProviderInfo(req.ProviderKey, installedVersion, IsMissing: false));
        }
    }

    return results;
}
```

Create `src/FalkForge.Engine/Detection/UnsatisfiedProviderInfo.cs`:
```csharp
namespace FalkForge.Engine.Detection;

internal sealed record UnsatisfiedProviderInfo(
    string ProviderKey,
    string? InstalledVersion,
    bool IsMissing);
```

**Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj --filter "DependencyDetectorTests"`
Expected: All PASS

**Step 6: Commit**

```bash
git add src/FalkForge.Engine/Detection/DependencyDetector.cs src/FalkForge.Engine/Detection/UnsatisfiedProviderInfo.cs src/FalkForge.Engine.Protocol/Manifest/ManifestDependencyRequirement.cs tests/FalkForge.Engine.Tests/Detection/DependencyDetectorTests.cs
git commit -m "feat(Engine): add runtime dependency provider version checking"
```

---

### Task C4: Wire unsatisfied providers into DetectingHandler

**Files:**
- Modify: `src/FalkForge.Engine/Phases/DetectingHandler.cs`
- Modify: `src/FalkForge.Engine/EngineContext.cs`
- Modify: `src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs` (add DependencyRequirements)
- Test: `tests/FalkForge.Engine.Tests/Phases/DetectingHandlerTests.cs`

**Step 1: Add UnsatisfiedProviders to EngineContext**

Add to `EngineContext.cs`:
```csharp
/// <summary>
/// Dependency providers that are missing or have unsatisfactory versions.
/// Populated during detection. Empty when all requirements are met.
/// </summary>
internal IReadOnlyList<UnsatisfiedProviderInfo> UnsatisfiedProviders { get; set; } = [];
```

**Step 2: Add DependencyRequirements to InstallerManifest**

Check if `InstallerManifest` already has this field. If not, add:
```csharp
public ManifestDependencyRequirement[] DependencyRequirements { get; init; } = [];
```

**Step 3: Wire into DetectingHandler**

Add after the `DependencyBlockers` detection (around line 84):
```csharp
// Detect unsatisfied dependency providers (version requirements)
context.UnsatisfiedProviders = DependencyDetector.DetectUnsatisfiedProviders(
    context.Manifest.DependencyRequirements,
    context.Platform.Registry);
```

**Step 4: Log warnings for unsatisfied providers**

Add after the detection:
```csharp
foreach (var unsatisfied in context.UnsatisfiedProviders)
{
    if (unsatisfied.IsMissing)
    {
        context.Logger.Warning("Dependency",
            $"Required dependency provider '{unsatisfied.ProviderKey}' is not installed.");
    }
    else
    {
        context.Logger.Warning("Dependency",
            $"Dependency provider '{unsatisfied.ProviderKey}' version {unsatisfied.InstalledVersion} does not satisfy requirements.");
    }
}
```

**Step 5: Run all engine tests**

Run: `dotnet test tests/FalkForge.Engine.Tests/FalkForge.Engine.Tests.csproj`
Expected: All PASS

**Step 6: Commit**

```bash
git add src/FalkForge.Engine/Phases/DetectingHandler.cs src/FalkForge.Engine/EngineContext.cs src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs
git commit -m "feat(Engine): wire unsatisfied dependency detection into engine lifecycle"
```

---

### Task C5: Emit DependencyRequirements from BundleCompiler manifest

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` (or equivalent)
- Modify: `src/FalkForge.Compiler.Bundle/BundleModel.cs` (add consumer requirements)
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs`
- Test: appropriate test file

**Step 1: Add Requires DSL to BundleBuilder**

The bundle builder needs a way for users to declare dependency requirements that get serialized into the manifest.

Add to `BundleBuilder`:
```csharp
public BundleBuilder RequiresDependency(string providerKey, Action<DependencyRequirementBuilder> configure)
```

This task is implementation-specific and depends on the exact ManifestGenerator structure. The implementing agent should:
1. Read ManifestGenerator to understand manifest serialization
2. Add DependencyRequirements to BundleModel
3. Add RequiresDependency to BundleBuilder
4. Serialize to manifest JSON
5. Test round-trip

**Step 2: Commit**

```bash
git commit -m "feat(Bundle): emit dependency requirements in bundle manifest"
```

---

## Execution Order

Tasks can be parallelized as follows:

```
A1 (demo) ────────────────────────→ done

B1 (FeaturePersistence) → B2 (FeatureDetector) → B3 (edge cases) → done

C1 (VersionRange) → C2 (DependencyChecker) → C3 (DependencyDetector) → C4 (DetectingHandler) → C5 (BundleBuilder) → done
```

- A1 is fully independent
- B-chain and C-chain are independent of each other
- Maximum parallelism: A1 + B1 + C1 simultaneously

## Summary

| Task | Description | New Files | Modified Files | Est. Tests |
|------|-------------|-----------|----------------|------------|
| A1 | Signing demo project | 4 | 0 | 0 (demo) |
| B1 | FeaturePersistence.LoadFromRelatedBundle | 0 | 2 | 2 |
| B2 | FeatureDetector migration logic | 0 | 3 | 1 |
| B3 | Edge case tests | 0 | 1 | 3 |
| C1 | VersionRange | 1 | 0 | 3 |
| C2 | DependencyChecker | 2 | 0 | 4 |
| C3 | DependencyDetector.DetectUnsatisfiedProviders | 2 | 1 | 3 |
| C4 | Wire into DetectingHandler | 0 | 3 | 0 |
| C5 | Bundle manifest emission | 0 | 3 | 1 |
| **Total** | | **9** | **13** | **~17** |
