using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// End-to-end wiring of the JSON <c>signing</c> section through <see cref="BuildCommand"/>:
/// a configured provider must make <c>forge build</c> emit a bundle whose integrity manifest
/// signature actually verifies (the C17 seam really signs — not just parses), an unresolvable
/// signing config must fail the build closed, and a config WITHOUT signing must keep the
/// existing MSI-only behavior byte-for-byte (no bundle appears).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("SourceDateEpoch")]
public sealed class BuildCommandSigningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<string> _envVarsToClear = [];

    public BuildCommandSigningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ForgeBuildSigning_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var name in _envVarsToClear)
            Environment.SetEnvironmentVariable(name, null);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    private string SetEnv(string value)
    {
        var name = $"C20_BUILD_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, value);
        _envVarsToClear.Add(name);
        return name;
    }

    /// <summary>Writes a minimal buildable JSON config, optionally with a signing section.</summary>
    private string WriteConfig(string? signingJson)
    {
        var payloadDir = Path.Combine(_tempDir, "payload");
        Directory.CreateDirectory(payloadDir);
        File.WriteAllText(Path.Combine(payloadDir, "app.exe"), "fixture payload bytes");

        var signingSection = signingJson is null ? string.Empty : $""","signing": {signingJson}""";
        var jsonPath = Path.Combine(_tempDir, "installer.json");
        File.WriteAllText(jsonPath, $$"""
        {
            "product": {
                "name": "SigningFixtureApp",
                "manufacturer": "TestCorp",
                "version": "1.0.0",
                "upgradeCode": "21111111-2222-3333-4444-555555555555"
            },
            "features": [
                { "id": "Main", "default": true, "files": [ { "source": "payload/app.exe" } ] }
            ]{{signingSection}}
        }
        """);
        return jsonPath;
    }

    // noEngine defaults to true so these signing-focused tests stay hermetic: they must not
    // depend on a published NativeAOT engine being present on the machine. The engine-resolution
    // tests below pass noEngine: false to exercise the real default path deterministically via
    // the FALKFORGE_ENGINE_STUB environment variable.
    private (int ExitCode, TestConsoleOutput Console, string OutputDir) RunBuild(string jsonPath, bool noEngine = true)
    {
        var outputDir = Path.Combine(_tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var console = new TestConsoleOutput();
        var command = new BuildCommand(console);
        var settings = new BuildSettings { ProjectPath = jsonPath, OutputPath = outputDir, NoEngine = noEngine };
        var exitCode = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);
        return (exitCode, console, outputDir);
    }

    // ── regression: no signing section keeps the MSI-only path ───────────────

    [Fact]
    public void NoSigningSection_BuildsMsiOnly_NoBundleAppears()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (exitCode, _, outputDir) = RunBuild(WriteConfig(signingJson: null));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.NotEmpty(Directory.GetFiles(outputDir, "*.msi"));
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
    }

    [Fact]
    public void ProviderNone_BuildsMsiOnly_NoBundleAppears()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (exitCode, _, outputDir) = RunBuild(WriteConfig("""{ "provider": "none" }"""));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.NotEmpty(Directory.GetFiles(outputDir, "*.msi"));
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
    }

    // ── e2e: pem signing produces a bundle whose manifest signature verifies ─

    [Fact]
    public void PemSigning_ProducesBundleWithVerifiableManifestSignature()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pemPath = Path.Combine(_tempDir, "release.pem");
        File.WriteAllText(pemPath, key.ExportPkcs8PrivateKeyPem());

        var (exitCode, console, outputDir) = RunBuild(
            WriteConfig("""{ "provider": "pem", "keyPath": "release.pem" }"""));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.NotEmpty(Directory.GetFiles(outputDir, "*.msi"));

        var bundlePath = Assert.Single(Directory.GetFiles(outputDir, "*.exe"));
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);

        using var manifest = JsonDocument.Parse(content.Value.ManifestJsonBytes!);
        var signatureJson = manifest.RootElement.GetProperty("ManifestSignature").GetString();
        Assert.False(string.IsNullOrEmpty(signatureJson), "bundle manifest is not signed");

        var envelope = IntegrityEnvelopeCodec.Parse(signatureJson!);
        Assert.NotNull(envelope);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!), "manifest signature does not verify");

        // The signature must come from the configured key — not an ephemeral fallback.
        var entry = Assert.Single(envelope!.Signatures);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())),
            entry.Fingerprint);

        Assert.Contains(console.AllOutput, line => line.Contains("bundle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PemSigning_KeyFromEnvironment_ProducesVerifiableBundle()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var envName = SetEnv(key.ExportPkcs8PrivateKeyPem());

        var (exitCode, _, outputDir) = RunBuild(
            WriteConfig($$"""{ "provider": "pem", "keyEnv": "{{envName}}" }"""));

        Assert.Equal(ExitCodes.Success, exitCode);
        var bundlePath = Assert.Single(Directory.GetFiles(outputDir, "*.exe"));

        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess);
        using var manifest = JsonDocument.Parse(content.Value.ManifestJsonBytes!);
        var envelope = IntegrityEnvelopeCodec.Parse(manifest.RootElement.GetProperty("ManifestSignature").GetString()!);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())),
            Assert.Single(envelope!.Signatures).Fingerprint);
    }

    // ── e2e: hybrid pem signing carries classical + ML-DSA entries ───────────

    [Fact]
    public void PemHybridSigning_ProducesBundleWithClassicalAndPqSignatures_BothVerify()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");
        Assert.SkipUnless(MLDsa.IsSupported, "ML-DSA is not supported by the OS/CNG on this machine.");

        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var pq = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        File.WriteAllText(Path.Combine(_tempDir, "release.pem"), classical.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(_tempDir, "release-mldsa.pem"), pq.ExportPkcs8PrivateKeyPem());

        var (exitCode, _, outputDir) = RunBuild(WriteConfig(
            """{ "provider": "pem", "keyPath": "release.pem", "pqKeyPath": "release-mldsa.pem" }"""));

        Assert.Equal(ExitCodes.Success, exitCode);
        var bundlePath = Assert.Single(Directory.GetFiles(outputDir, "*.exe"));
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);

        using var manifest = JsonDocument.Parse(content.Value.ManifestJsonBytes!);
        var envelope = IntegrityEnvelopeCodec.Parse(
            manifest.RootElement.GetProperty("ManifestSignature").GetString()!)!;

        // Both entries present, classical first, from the CONFIGURED keys.
        Assert.Equal(2, envelope.Signatures.Count);
        Assert.Null(envelope.Signatures[0].Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(classical.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[0].Fingerprint);
        Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope), "classical signature does not verify");

        Assert.Equal(IntegrityEnvelopeCodec.MlDsa65AlgorithmId, envelope.Signatures[1].Algorithm);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(pq.ExportSubjectPublicKeyInfo())),
            envelope.Signatures[1].Fingerprint);
        var message = IntegrityEnvelopeCodec.ComputeSignedBytes(envelope.Files, envelope.Epoch, envelope.Revoked);
        using var pqPub = MLDsa.ImportSubjectPublicKeyInfo(
            Convert.FromBase64String(envelope.Signatures[1].PublicKey));
        Assert.True(pqPub.VerifyData(
            message,
            Convert.FromBase64String(envelope.Signatures[1].Signature),
            FalkForge.Signing.SignatureAlgorithms.ManifestContext), "ML-DSA signature does not verify");
    }

    [Fact]
    public void PemHybridSigning_UnsetPqKeyEnv_FailsBuildClosed()
    {
        // A hybrid config whose PQ env var is unset must fail the whole build — never quietly
        // produce a classical-only bundle the publisher believes is hybrid.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        using var classical = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var classicalEnv = SetEnv(classical.ExportPkcs8PrivateKeyPem());

        var (exitCode, console, outputDir) = RunBuild(WriteConfig($$"""
            { "provider": "pem", "keyEnv": "{{classicalEnv}}", "pqKeyEnv": "C20_UNSET_{{Guid.NewGuid():N}}" }
            """));

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
        Assert.Contains(console.Errors, e => e.Contains("JSN019", StringComparison.Ordinal));
        Assert.Contains(console.Errors, e => e.Contains("pqKeyEnv", StringComparison.Ordinal));
    }

    // ── engine stub policy: real engine by default, placeholder only on opt-in

    [Fact]
    public void NoEngineOptIn_PrintsPlaceholderStubNotRunnableWarning()
    {
        // With --no-engine the signed bundle wraps a design-time placeholder engine stub, so it
        // verifies but cannot install anything. The build must say so LOUDLY at build time —
        // but ONLY when the placeholder was explicitly chosen.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(_tempDir, "release.pem"), key.ExportPkcs8PrivateKeyPem());

        var (exitCode, console, _) = RunBuild(
            WriteConfig("""{ "provider": "pem", "keyPath": "release.pem" }"""), noEngine: true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains(console.AllOutput, line =>
            line.Contains("NOT a runnable installer", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultEngineResolution_EmbedsRealStub_AndDoesNotPrintPlaceholderWarning()
    {
        // The DEFAULT signed-bundle path must embed the resolved engine binary as the bundle's
        // PE front (a runnable self-extracting installer) and must NOT print the "NOT a runnable
        // installer" warning — that warning is reserved for the explicit --no-engine opt-in.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var engineDir = Path.Combine(_tempDir, "engine-drop");
        Directory.CreateDirectory(engineDir);
        var enginePath = Path.Combine(engineDir, "FalkForge.Engine.exe");
        var engineBytes = new byte[512];
        engineBytes[0] = (byte)'M';
        engineBytes[1] = (byte)'Z';
        File.WriteAllBytes(enginePath, engineBytes);
        // Mirror the publish layout: the elevation companion lives beside the engine so the
        // default build can embed it as a trust-covered payload.
        File.WriteAllBytes(
            Path.Combine(engineDir, FalkForge.Engine.Protocol.Bundle.EngineCompanionPayload.PackageId),
            [(byte)'M', (byte)'Z', 0xE1, 0xE7]);
        Environment.SetEnvironmentVariable(EngineStubLocator.EnvironmentVariableName, enginePath);
        _envVarsToClear.Add(EngineStubLocator.EnvironmentVariableName);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(_tempDir, "release.pem"), key.ExportPkcs8PrivateKeyPem());

        var (exitCode, console, outputDir) = RunBuild(
            WriteConfig("""{ "provider": "pem", "keyPath": "release.pem" }"""), noEngine: false);

        Assert.Equal(ExitCodes.Success, exitCode);
        var bundlePath = Assert.Single(Directory.GetFiles(outputDir, "*.exe"));
        using (var stream = File.OpenRead(bundlePath))
        {
            var prefix = new byte[2];
            stream.ReadExactly(prefix);
            Assert.Equal((byte)'M', prefix[0]);
            Assert.Equal((byte)'Z', prefix[1]);
        }

        Assert.DoesNotContain(console.AllOutput, line =>
            line.Contains("NOT a runnable installer", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultEngineResolution_Unresolvable_FailsBuildClosed()
    {
        // Without --no-engine an unresolvable engine must FAIL the build — never silently ship
        // a bundle that verifies but cannot install.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        Environment.SetEnvironmentVariable(
            EngineStubLocator.EnvironmentVariableName,
            Path.Combine(_tempDir, "no-such-engine.exe"));
        _envVarsToClear.Add(EngineStubLocator.EnvironmentVariableName);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(Path.Combine(_tempDir, "release.pem"), key.ExportPkcs8PrivateKeyPem());

        var (exitCode, console, outputDir) = RunBuild(
            WriteConfig("""{ "provider": "pem", "keyPath": "release.pem" }"""), noEngine: false);

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
        Assert.Contains(console.Errors, e =>
            e.Contains(EngineStubLocator.EnvironmentVariableName, StringComparison.Ordinal));
    }

    // ── fail-closed: unresolvable signing config must fail the build ─────────

    [Fact]
    public void PemSigning_UnsetKeyEnv_FailsBuild_NoArtifactsSigned()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (exitCode, console, outputDir) = RunBuild(
            WriteConfig($$"""{ "provider": "pem", "keyEnv": "C20_UNSET_{{Guid.NewGuid():N}}" }"""));

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
        Assert.Contains(console.Errors, e => e.Contains("JSN019", StringComparison.Ordinal));
    }

    [Fact]
    public void SecretShapedBearerTokenEnv_FailsClosed_ConsoleNeverEchoesTheSecret()
    {
        // A real alnum-only token mispasted into bearerTokenEnv passes the loader's charset
        // check (JSN016 cannot classify it), the env var of that literal name is unset, and
        // the fail-closed error surfaces on the console — where it must NOT echo the token
        // into stdout / CI logs. This is the end-to-end leak regression.
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        const string secretShapedToken = "ghp_FakeLeakCanary0123456789abcdef";

        var (exitCode, console, outputDir) = RunBuild(WriteConfig($$"""
            {
                "provider": "signserver",
                "baseUrl": "https://sign.example.com",
                "worker": "PlainSigner",
                "authMode": "bearer",
                "bearerTokenEnv": "{{secretShapedToken}}"
            }
            """));

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
        Assert.Contains(console.Errors, e => e.Contains("JSN019", StringComparison.Ordinal));
        Assert.Contains(console.Errors, e => e.Contains("bearerTokenEnv", StringComparison.Ordinal));
        Assert.DoesNotContain(console.AllOutput, line =>
            line.Contains(secretShapedToken, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnknownSigningProvider_FailsValidation()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var (exitCode, console, outputDir) = RunBuild(WriteConfig("""{ "provider": "hsm" }"""));

        Assert.Equal(ExitCodes.ValidationFailure, exitCode);
        Assert.Empty(Directory.GetFiles(outputDir, "*.exe"));
        Assert.Contains(console.Errors, e => e.Contains("JSN015", StringComparison.Ordinal));
    }
}
