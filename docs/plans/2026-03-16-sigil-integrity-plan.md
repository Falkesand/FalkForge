# Sigil Integrity & SBOM Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add build-time payload signing via Sigil.Sign and runtime integrity verification, with SBOM generation in SPDX and CycloneDX formats.

**Architecture:** Opportunistic signing at build time (only when `sigil` CLI is on PATH). Manifest signature and SBOM attestation embedded in MSI custom table and bundle footer section. Runtime verification in engine using built-in .NET SHA-256 + ECDSA — no Sigil dependency at runtime.

**Tech Stack:** C# 13, .NET 10, xUnit 2.9.3, System.Security.Cryptography (ECDSA), Sigil.Sign CLI, SPDX 2.3, CycloneDX 1.5

---

### Task 1: Core Models — IntegrityConfiguration + SbomFormat + ErrorKind

**Files:**
- Create: `src/FalkForge.Core/Models/IntegrityConfiguration.cs`
- Create: `src/FalkForge.Core/Models/SbomFormat.cs`
- Modify: `src/FalkForge.Core/ErrorKind.cs`
- Test: `tests/FalkForge.Core.Tests/Models/IntegrityConfigurationTests.cs`

**Step 1: Write failing test**

```csharp
namespace FalkForge.Core.Tests.Models;

using FalkForge.Models;
using Xunit;

public class IntegrityConfigurationTests
{
    [Fact]
    public void Default_Configuration_Uses_Spdx_And_No_Key()
    {
        var config = new IntegrityConfiguration();
        Assert.Null(config.SigningKeyPath);
        Assert.Null(config.CertStoreThumbprint);
        Assert.Null(config.VaultProvider);
        Assert.Null(config.VaultKeyRef);
        Assert.Equal(SbomFormat.Spdx, config.SbomFormat);
    }

    [Fact]
    public void Can_Set_All_Properties()
    {
        var config = new IntegrityConfiguration
        {
            SigningKeyPath = "key.pem",
            CertStoreThumbprint = "ABC123",
            VaultProvider = "azure",
            VaultKeyRef = "my-key",
            SbomFormat = SbomFormat.CycloneDx
        };
        Assert.Equal("key.pem", config.SigningKeyPath);
        Assert.Equal(SbomFormat.CycloneDx, config.SbomFormat);
    }
}
```

**Step 2: Run test to verify failure**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/sigil-integrity/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~IntegrityConfigurationTests"`
Expected: FAIL — types don't exist

**Step 3: Implement**

`src/FalkForge.Core/Models/SbomFormat.cs`:
```csharp
namespace FalkForge.Models;

public enum SbomFormat
{
    Spdx,
    CycloneDx
}
```

`src/FalkForge.Core/Models/IntegrityConfiguration.cs`:
```csharp
namespace FalkForge.Models;

public sealed class IntegrityConfiguration
{
    public string? SigningKeyPath { get; init; }
    public string? CertStoreThumbprint { get; init; }
    public string? VaultProvider { get; init; }
    public string? VaultKeyRef { get; init; }
    public SbomFormat SbomFormat { get; init; } = SbomFormat.Spdx;
}
```

`src/FalkForge.Core/ErrorKind.cs` — add `IntegrityError` after the last existing value.

**Step 4: Run test to verify pass**

Run: `dotnet test D:/Git/FalkInstaller/.worktrees/sigil-integrity/tests/FalkForge.Core.Tests/FalkForge.Core.Tests.csproj --filter "FullyQualifiedName~IntegrityConfigurationTests"`
Expected: PASS

**Step 5: Commit**

```bash
git -C D:/Git/FalkInstaller/.worktrees/sigil-integrity add -A
git -C D:/Git/FalkInstaller/.worktrees/sigil-integrity commit -m "feat(integrity): add IntegrityConfiguration model, SbomFormat enum, IntegrityError kind"
```

---

### Task 2: IntegrityBuilder + PackageBuilder/BundleBuilder Integration

**Files:**
- Create: `src/FalkForge.Core/Builders/IntegrityBuilder.cs`
- Modify: `src/FalkForge.Core/Models/PackageModel.cs` — add `IntegrityConfiguration? Integrity` property
- Modify: `src/FalkForge.Core/Builders/PackageBuilder.cs` — add `Integrity(Action<IntegrityBuilder>)` method + `_integrity` field + wire to Build()
- Modify: `src/FalkForge.Compiler.Bundle/BundleModel.cs` — add `IntegrityConfiguration? Integrity` property
- Modify: `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` — add `Integrity(Action<IntegrityBuilder>)` method
- Test: `tests/FalkForge.Core.Tests/Builders/IntegrityBuilderTests.cs`

**Step 1: Write failing test**

```csharp
namespace FalkForge.Core.Tests.Builders;

using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

public class IntegrityBuilderTests
{
    [Fact]
    public void Default_Build_Returns_Spdx_No_Key()
    {
        var builder = new IntegrityBuilder();
        var config = builder.Build();
        Assert.Equal(SbomFormat.Spdx, config.SbomFormat);
        Assert.Null(config.SigningKeyPath);
    }

    [Fact]
    public void SigningKey_Sets_Path()
    {
        var builder = new IntegrityBuilder();
        builder.SigningKey("my-key.pem");
        var config = builder.Build();
        Assert.Equal("my-key.pem", config.SigningKeyPath);
    }

    [Fact]
    public void CertStore_Sets_Thumbprint()
    {
        var builder = new IntegrityBuilder();
        builder.CertStore("AABB");
        var config = builder.Build();
        Assert.Equal("AABB", config.CertStoreThumbprint);
    }

    [Fact]
    public void Vault_Sets_Provider_And_Key()
    {
        var builder = new IntegrityBuilder();
        builder.Vault("azure", "my-key");
        var config = builder.Build();
        Assert.Equal("azure", config.VaultProvider);
        Assert.Equal("my-key", config.VaultKeyRef);
    }

    [Fact]
    public void Sbom_Sets_Format()
    {
        var builder = new IntegrityBuilder();
        builder.Sbom(SbomFormat.CycloneDx);
        var config = builder.Build();
        Assert.Equal(SbomFormat.CycloneDx, config.SbomFormat);
    }

    [Fact]
    public void PackageBuilder_Integrity_Wires_Through()
    {
        var package = new PackageBuilder("Test", "Mfg")
            .Integrity(i => i.SigningKey("test.pem").Sbom(SbomFormat.CycloneDx));
        // Call Build and check Integrity property exists
        // This just verifies the builder method compiles and chains
    }
}
```

**Step 2: Run test — expected FAIL**

**Step 3: Implement**

`src/FalkForge.Core/Builders/IntegrityBuilder.cs`:
```csharp
namespace FalkForge.Builders;

using FalkForge.Models;

public sealed class IntegrityBuilder
{
    private string? _signingKeyPath;
    private string? _certStoreThumbprint;
    private string? _vaultProvider;
    private string? _vaultKeyRef;
    private SbomFormat _sbomFormat = SbomFormat.Spdx;

    public IntegrityBuilder SigningKey(string path) { _signingKeyPath = path; return this; }
    public IntegrityBuilder CertStore(string thumbprint) { _certStoreThumbprint = thumbprint; return this; }
    public IntegrityBuilder Vault(string provider, string keyRef) { _vaultProvider = provider; _vaultKeyRef = keyRef; return this; }
    public IntegrityBuilder Sbom(SbomFormat format) { _sbomFormat = format; return this; }

    internal IntegrityConfiguration Build() => new()
    {
        SigningKeyPath = _signingKeyPath,
        CertStoreThumbprint = _certStoreThumbprint,
        VaultProvider = _vaultProvider,
        VaultKeyRef = _vaultKeyRef,
        SbomFormat = _sbomFormat
    };
}
```

Modify PackageModel: add `public IntegrityConfiguration? Integrity { get; init; }`
Modify PackageBuilder: add `private IntegrityConfiguration? _integrity;` field, add `Integrity(Action<IntegrityBuilder> configure)` method following the SigningOptions pattern, wire `_integrity` into Build().
Modify BundleModel: add `public IntegrityConfiguration? Integrity { get; init; }`
Modify BundleBuilder: add same pattern.

**Step 4: Run test — expected PASS**
**Step 5: Commit** `feat(integrity): add IntegrityBuilder with PackageBuilder/BundleBuilder integration`

---

### Task 3: SigilDetector — Check if Sigil CLI is Available

**Files:**
- Create: `src/FalkForge.Compiler.Msi/Signing/SigilDetector.cs`
- Test: `tests/FalkForge.Compiler.Msi.Tests/Signing/SigilDetectorTests.cs`

**Step 1: Write failing test**

```csharp
namespace FalkForge.Compiler.Msi.Tests.Signing;

using FalkForge.Compiler.Msi.Signing;
using Xunit;

public class SigilDetectorTests
{
    [Fact]
    public void IsAvailable_Returns_Bool()
    {
        // This test verifies the detector runs without crashing
        // The actual result depends on whether sigil is installed
        var result = SigilDetector.IsAvailable();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetVersion_Returns_String_Or_Null()
    {
        var version = SigilDetector.GetVersion();
        // null if not installed, version string if installed
        if (SigilDetector.IsAvailable())
            Assert.NotNull(version);
        else
            Assert.Null(version);
    }
}
```

**Step 2: Run test — FAIL**

**Step 3: Implement**

`src/FalkForge.Compiler.Msi/Signing/SigilDetector.cs`:
```csharp
namespace FalkForge.Compiler.Msi.Signing;

using System.Diagnostics;

internal static class SigilDetector
{
    private static bool? _isAvailable;
    private static string? _version;

    internal static bool IsAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;
        try
        {
            var psi = new ProcessStartInfo("sigil", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) { _isAvailable = false; return false; }
            _version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            _isAvailable = process.ExitCode == 0;
        }
        catch
        {
            _isAvailable = false;
        }
        return _isAvailable.Value;
    }

    internal static string? GetVersion() { IsAvailable(); return _version; }

    internal static void Reset() { _isAvailable = null; _version = null; }
}
```

**Step 4: Run test — PASS**
**Step 5: Commit** `feat(integrity): add SigilDetector for CLI availability check`

---

### Task 4: SigilSigner — Run sigil sign-manifest and sigil attest

**Files:**
- Create: `src/FalkForge.Compiler.Msi/Signing/SigilSigner.cs`
- Test: `tests/FalkForge.Compiler.Msi.Tests/Signing/SigilSignerTests.cs`

The SigilSigner orchestrates two Sigil CLI calls:
1. `sigil sign-manifest <temp-dir>` — signs a directory of payload files
2. `sigil attest <artifact> --predicate <sbom.json> --type spdx|cyclonedx` — wraps SBOM

**Step 1: Write failing test**

Test the argument building logic without actually calling sigil (unit-testable):

```csharp
namespace FalkForge.Compiler.Msi.Tests.Signing;

using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using Xunit;

public class SigilSignerTests
{
    [Fact]
    public void BuildSignManifestArgs_Ephemeral_Key()
    {
        var args = SigilSigner.BuildSignManifestArgs("/tmp/payloads", null);
        Assert.Contains("sign-manifest", args);
        Assert.Contains("/tmp/payloads", args);
        Assert.DoesNotContain("--key", args);
    }

    [Fact]
    public void BuildSignManifestArgs_With_Key_Path()
    {
        var config = new IntegrityConfiguration { SigningKeyPath = "my.pem" };
        var args = SigilSigner.BuildSignManifestArgs("/tmp/payloads", config);
        Assert.Contains("--key", args);
        Assert.Contains("my.pem", args);
    }

    [Fact]
    public void BuildSignManifestArgs_With_CertStore()
    {
        var config = new IntegrityConfiguration { CertStoreThumbprint = "AABB" };
        var args = SigilSigner.BuildSignManifestArgs("/tmp/payloads", config);
        Assert.Contains("--cert-store", args);
        Assert.Contains("AABB", args);
    }

    [Fact]
    public void BuildSignManifestArgs_With_Vault()
    {
        var config = new IntegrityConfiguration { VaultProvider = "azure", VaultKeyRef = "key1" };
        var args = SigilSigner.BuildSignManifestArgs("/tmp/payloads", config);
        Assert.Contains("--vault", args);
        Assert.Contains("azure", args);
        Assert.Contains("--vault-key", args);
        Assert.Contains("key1", args);
    }

    [Fact]
    public void BuildAttestArgs_Spdx()
    {
        var args = SigilSigner.BuildAttestArgs("output.msi", "/tmp/sbom.json", SbomFormat.Spdx, null);
        Assert.Contains("attest", args);
        Assert.Contains("--type", args);
        Assert.Contains("spdx", args);
    }

    [Fact]
    public void BuildAttestArgs_CycloneDx()
    {
        var args = SigilSigner.BuildAttestArgs("output.msi", "/tmp/sbom.json", SbomFormat.CycloneDx, null);
        Assert.Contains("cyclonedx", args);
    }
}
```

**Step 2: Run test — FAIL**

**Step 3: Implement**

`src/FalkForge.Compiler.Msi/Signing/SigilSigner.cs` — a static class with:
- `BuildSignManifestArgs(string payloadDir, IntegrityConfiguration? config)` — returns `List<string>` of CLI args
- `BuildAttestArgs(string artifactPath, string predicatePath, SbomFormat format, IntegrityConfiguration? config)` — returns `List<string>`
- `RunSignManifest(string payloadDir, string outputPath, IntegrityConfiguration? config)` → `Result<string>` (returns the signature JSON path)
- `RunAttest(string artifactPath, string sbomJsonPath, SbomFormat format, string outputPath, IntegrityConfiguration? config)` → `Result<string>`
- Internal: `RunSigil(List<string> args)` → `Result<string>` — calls `sigil` via Process

**Step 4: Run test — PASS**
**Step 5: Commit** `feat(integrity): add SigilSigner with sign-manifest and attest orchestration`

---

### Task 5: SBOM Generator — SPDX and CycloneDX JSON

**Files:**
- Create: `src/FalkForge.Core/Sbom/SpdxSbomGenerator.cs`
- Create: `src/FalkForge.Core/Sbom/IntegritySbomGenerator.cs`
- Test: `tests/FalkForge.Core.Tests/Sbom/IntegritySbomGeneratorTests.cs`

The generator takes a list of file entries (name, hash, size) + package metadata and outputs SBOM JSON.

**Step 1: Write failing test**

Tests for generating correct SPDX 2.3 and CycloneDX 1.5 JSON from file metadata. Verify JSON contains required fields (spdxVersion, bomFormat, file entries with SHA-256 hashes).

**Step 2: Run — FAIL**
**Step 3: Implement** — two static methods: `GenerateSpdx(...)` and `GenerateCycloneDx(...)` returning JSON strings.
**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add SPDX and CycloneDX SBOM generators`

---

### Task 6: MSI Integration — _FalkForgeIntegrity Table + MsiCompiler Step

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/Tables/MsiTableDefinitions.cs` — add CreateFalkForgeIntegrityTable
- Modify: `src/FalkForge.Compiler.Msi/Tables/IntegrityTableEmitter.cs` — integrity emission lives here post-Phase 9 cutover (commit 1c40837)
- Modify: `src/FalkForge.Compiler.Msi/MsiCompiler.cs` — add signing step after code signing
- Test: `tests/FalkForge.Compiler.Msi.Tests/Tables/IntegrityTableEmissionTests.cs`

**Step 1: Write failing test** — verify EmitIntegrity writes two rows to the custom table with correct Id/Format/Data values.

**Step 2: Run — FAIL**
**Step 3: Implement** — table definition, emission method, compiler step that detects Sigil, signs, generates SBOM, embeds, and writes sidecar files.
**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add _FalkForgeIntegrity MSI table and MsiCompiler signing step`

---

### Task 7: Bundle Integration — IntegritySection in Footer

**Files:**
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/BundleCompiler.cs` — add signing step
- Modify: `src/FalkForge.Compiler.Bundle/Compilation/PayloadEmbedder.cs` — extend footer with IntegritySection
- Test: `tests/FalkForge.Compiler.Bundle.Tests/Compilation/IntegritySectionTests.cs`

**Step 1: Write failing test** — verify IntegritySection is written with FINT magic, correct lengths, and JSON content. Verify footer offset table includes integrity offset.

**Step 2: Run — FAIL**
**Step 3: Implement** — add IntegritySection writing after TOC in PayloadEmbedder, update footer offset. BundleCompiler calls SigilSigner after embedding.
**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add IntegritySection to bundle footer with manifest signature and SBOM`

---

### Task 8: Engine Runtime Verification

**Files:**
- Modify: `src/FalkForge.Engine/Phases/DetectingHandler.cs` — add integrity verification
- Create: `src/FalkForge.Engine/Integrity/IntegrityVerifier.cs`
- Test: `tests/FalkForge.Engine.Tests/Integrity/IntegrityVerifierTests.cs`

**Step 1: Write failing test**

Test IntegrityVerifier:
- Valid manifest + matching hashes → success
- Valid manifest + mismatched hash → failure with INT002
- Invalid signature → failure with INT001
- No manifest (null) → success (backward compatible)
- Manifest with missing public key → warning INT003

**Step 2: Run — FAIL**
**Step 3: Implement**

`IntegrityVerifier`:
- `Verify(InstallerManifest manifest, string extractedPayloadDir)` → `Result<Unit>`
- Parse manifest.ManifestSignature JSON to extract public key + file hashes
- Verify ECDSA-P256 signature using `ECDsa.VerifyData()` from System.Security.Cryptography
- For each file in manifest: compute SHA-256, compare with expected hash
- Return `Result.Failure(IntegrityError, "INT002: ...")` on mismatch

In DetectingHandler: call `IntegrityVerifier.Verify()` after payload extraction, before returning to Planning.

**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add runtime integrity verification in engine DetectingHandler`

---

### Task 9: Bundle --sbom CLI Flag

**Files:**
- Modify: `src/FalkForge.Engine/EngineHost.cs` — add --sbom early exit
- Test: `tests/FalkForge.Engine.Tests/EngineHostSbomExtractionTests.cs`

**Step 1: Write failing test** — verify EngineHost parses --sbom arg, extracts SBOM from manifest, writes to file.

**Step 2: Run — FAIL**
**Step 3: Implement** — in EngineHost.RunAsync, before entering state machine, check for `--sbom <path>`. If found: read manifest.SbomAttestation, write JSON to path, return exit code 0.
**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add --sbom flag to bundle engine for SBOM extraction`

---

### Task 10: CLI Integration — forge inspect --sbom + --no-sign

**Files:**
- Modify: `src/FalkForge.Cli/Commands/InspectCommand.cs` — add --sbom subcommand
- Modify: `src/FalkForge.Cli/Settings/BuildSettings.cs` — add --no-sign flag
- Modify: `src/FalkForge.Cli/Commands/BuildCommand.cs` — pass --no-sign to compiler

**Step 1: Write failing test** — verify BuildSettings has NoSign property, InspectCommand supports --sbom flag.

**Step 2: Run — FAIL**
**Step 3: Implement**

`forge inspect --sbom output.msi`:
- Open MSI database
- Read `_FalkForgeIntegrity` table, find row with Id=`SbomAttestation`
- Write Data column to stdout (or file if --output specified)
- If table doesn't exist: print "No SBOM available", exit code 1

`forge build --no-sign`:
- Add `[CommandOption("--no-sign")] public bool NoSign { get; set; }` to BuildSettings
- In BuildCommand: set environment variable `FALKFORGE_NO_SIGN=1`
- MsiCompiler/BundleCompiler check this env var before calling SigilSigner

**Step 4: Run — PASS**
**Step 5: Commit** `feat(integrity): add forge inspect --sbom and forge build --no-sign`

---

### Task 11: Full Verification

**Step 1:** Build the entire solution: `dotnet build D:/Git/FalkInstaller/.worktrees/sigil-integrity/FalkForge.slnx`
**Step 2:** Run all tests: `dotnet test` for Core, Compiler.Msi, Compiler.Bundle, Engine test projects
**Step 3:** Manual smoke test: build demo/01-hello-world with Sigil installed, verify .sig.json and .sbom.json sidecar files are created
**Step 4:** Commit any fixes
